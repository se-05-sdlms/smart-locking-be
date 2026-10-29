using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using System.Text.RegularExpressions;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ResidentAccountChangeService(ApplicationDbContext db, IPasswordHashService passwords,
    IOtpService otp, TimeProvider clock) : IResidentAccountChangeService
{
    public async Task<PagedResult<ResidentAccountChangeResponse>> GetAsync(Guid actorId, int pageNumber, int pageSize, CancellationToken ct)
    {
        User actor = await ActorAsync(actorId, false, ct);
        if (pageNumber < 1 || pageSize is < 1 or > 100) throw BadRequestException.For("pageSize", "Invalid pagination.");
        var query = Cases().Where(c => actor.Role == UserRole.Administrator || c.ResidentUserId == actorId);
        int count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(c => c.CreatedAt).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var results = new List<ResidentAccountChangeResponse>();
        foreach (var row in rows) results.Add(await MapAsync(row, ct));
        return new(results, count, pageNumber, pageSize);
    }

    public async Task<ResidentAccountChangeResponse> GetAsync(Guid actorId, Guid id, CancellationToken ct)
    {
        User actor = await ActorAsync(actorId, false, ct);
        return await MapAsync(await CaseAsync(actor, id, ct), ct);
    }

    public async Task<AccountChangeEligibilityResponse> EligibilityAsync(Guid actorId, Guid targetLockerId, CancellationToken ct)
    {
        await ActorAsync(actorId, true, ct);
        var profile = await ProfileAsync(actorId, ct);
        var blockers = await LockerBlockersAsync(profile, targetLockerId, ct);
        return new(targetLockerId, blockers.Count == 0, blockers);
    }

    public async Task<ResidentAccountChangeResponse> CreateLockerAsync(Guid actorId, CreateLockerChangeRequest request, CancellationToken ct)
    {
        await ActorAsync(actorId, true, ct);
        string reason = Text(request.Reason);
        await using var transaction = await LockResidentAsync(actorId, ct);
        ResidentProfile profile = await ProfileAsync(actorId, ct);
        Locker target = await db.Lockers.SingleOrDefaultAsync(l => l.Id == request.TargetLockerId, ct)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, "Target locker not found.");
        if (target.Id == profile.RegisteredLockerId) throw BadRequestException.For("targetLockerId", "Choose a different locker.");
        if (!await db.OperationalLockers().AnyAsync(l => l.Id == target.Id, ct))
            throw new ConflictException(LockerErrorCodes.NotOperational, "Target locker is unavailable for registration.");
        ResidentAccountChange? existing = await Cases().SingleOrDefaultAsync(c => c.ResidentUserId == actorId && c.Kind == "Locker" && c.Status == "Submitted", ct);
        if (existing is not null)
        {
            if (existing.TargetLockerId != target.Id) throw new ConflictException("account-change/pending", "Cancel the pending locker request before choosing another locker.");
            return await MapAsync(existing, ct);
        }
        var item = New(actorId, "Locker", reason);
        item.OriginalLockerId = profile.RegisteredLockerId;
        item.TargetLockerId = target.Id;
        item.TargetLocker = target;
        db.Set<ResidentAccountChange>().Add(item);
        AddEvent(item, actorId, "Submitted", "Locker change requested. Goods remain at their physical location; purchased benefits remain unchanged.");
        await SaveAsync(transaction, ct);
        return await MapAsync(item, ct);
    }

    public async Task RequestPhoneOtpAsync(Guid actorId, RequestPhoneChangeOtpRequest request, CancellationToken ct)
    {
        await ActorAsync(actorId, true, ct);
        string phone = Phone(request.NewPhoneNumber);
        await using var transaction = await LockResidentAsync(actorId, ct);
        User resident = await ActorAsync(actorId, true, ct);
        VerifyPassword(resident, request.CurrentPassword);
        await EnsurePhoneAvailableAsync(resident, phone, ct);
        DateTimeOffset now = clock.GetUtcNow();
        if (await db.OtpChallenges.AnyAsync(c => c.UserId == actorId && c.Purpose == OtpPurpose.PhoneChange && c.CreatedAt > now.AddMinutes(-1), ct))
            throw new ConflictException("account-change/otp-cooldown", "Wait one minute before requesting another code.");
        if (await db.OtpChallenges.CountAsync(c => c.UserId == actorId && c.Purpose == OtpPurpose.PhoneChange && c.CreatedAt > now.AddHours(-1), ct) >= 5)
            throw new ConflictException("account-change/otp-limit", "Too many OTP requests. Try again later.");
        // The configured transport must acknowledge delivery before the challenge is persisted.
        await otp.IssueAsync(phone, OtpPurpose.PhoneChange, actorId, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task<ResidentAccountChangeResponse> CreatePhoneAsync(Guid actorId, CreatePhoneChangeRequest request, CancellationToken ct)
    {
        await ActorAsync(actorId, true, ct);
        string phone = Phone(request.NewPhoneNumber);
        string reason = Text(request.Reason);
        await using var transaction = await LockResidentAsync(actorId, ct);
        User resident = await ActorAsync(actorId, true, ct);
        VerifyPassword(resident, request.CurrentPassword);
        await EnsurePhoneAvailableAsync(resident, phone, ct);
        ResidentAccountChange? existing = await Cases().SingleOrDefaultAsync(c => c.ResidentUserId == actorId && c.Kind == "Phone" && c.Status == "Submitted", ct);
        if (existing is not null && existing.NewPhoneNumber != phone)
            throw new ConflictException("account-change/pending", "Cancel the pending phone request before choosing another number.");
        // A lost HTTP response must not turn a successful submission into an OTP replay error.
        if (existing is not null && existing.OriginalPhoneNumber == resident.PhoneNumber && existing.PhoneVerificationExpiresAt > clock.GetUtcNow())
            return await MapAsync(existing, ct);
        try
        {
            await otp.VerifyAndConsumeAsync(phone, request.OtpCode, OtpPurpose.PhoneChange, actorId, ct);
        }
        catch (BadRequestException)
        {
            // Preserve failed-attempt counters and lockouts even though the request was rejected.
            if (transaction is not null) await transaction.CommitAsync(ct);
            throw;
        }
        ResidentAccountChange item = existing ?? New(actorId, "Phone", reason);
        if (existing is null) db.Set<ResidentAccountChange>().Add(item);
        item.OriginalPhoneNumber = resident.PhoneNumber;
        item.NewPhoneNumber = phone;
        // Ownership proof cannot wait indefinitely for approval; a new OTP renews this same request.
        item.PhoneVerificationExpiresAt = clock.GetUtcNow().AddHours(24);
        item.Reason = reason;
        AddEvent(item, actorId, existing is null ? "Submitted" : "PhoneReverified", "Current password and the new phone OTP verified. Administrator approval is still required within 24 hours.");
        await SaveAsync(transaction, ct);
        return await MapAsync(item, ct);
    }

    public async Task<ResidentAccountChangeResponse> CancelAsync(Guid actorId, Guid id, CancellationToken ct)
    {
        User actor = await ActorAsync(actorId, true, ct);
        await using var transaction = await LockResidentAsync(actorId, ct);
        var item = await CaseAsync(actor, id, ct);
        if (item.Status == "Cancelled") return await MapAsync(item, ct);
        if (item.Status != "Submitted") throw new ConflictException("account-change/final", "This request has already been reviewed.");
        item.Status = "Cancelled";
        AddEvent(item, actorId, "Cancelled", "The resident cancelled this request.");
        await SaveAsync(transaction, ct);
        return await MapAsync(item, ct);
    }

    public async Task<ResidentAccountChangeResponse> ReviewAsync(Guid actorId, Guid id, ReviewAccountChangeRequest request, CancellationToken ct)
    {
        User actor = await ActorAsync(actorId, false, ct);
        if (actor.Role != UserRole.Administrator) throw new ForbiddenException("account-change/access-denied", "Administrator approval is required.");
        string reason = Text(request.Reason);
        var before = await CaseAsync(actor, id, ct);
        await using var transaction = await LockResidentAsync(before.ResidentUserId, ct);
        // Both the request and identity must be read after the resident lock, including retries.
        await db.Entry(before).ReloadAsync(ct);
        var item = await CaseAsync(actor, id, ct);
        if (item.Status != "Submitted") return await MapAsync(item, ct);
        User resident = await ActorAsync(item.ResidentUserId, true, ct);
        ResidentProfile profile = await ProfileAsync(resident.Id, ct);
        item.ReviewedByUserId = actorId;
        item.ReviewReason = reason;
        if (!request.Approve)
        {
            item.Status = "Rejected";
            AddEvent(item, actorId, "Rejected", reason);
        }
        else
        {
            List<AccountChangeBlocker> blockers;
            if (item.Kind == "Locker")
            {
                if (db.Database.IsRelational())
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Locker\" WHERE \"Id\" = {item.TargetLockerId} FOR UPDATE", ct);
                blockers = await LockerBlockersAsync(profile, item.TargetLockerId!.Value, ct);
                if (profile.RegisteredLockerId != item.OriginalLockerId)
                    blockers.Add(new("Account", item.Id, "The registered locker changed after this request was submitted. Cancel and create a new request."));
            }
            else blockers = await PhoneBlockersAsync(item, resident, ct);
            if (blockers.Count > 0)
            {
                AddEvent(item, actorId, "ApprovalBlocked", reason);
                await SaveAsync(transaction, ct);
                return await MapAsync(item, ct, blockers);
            }
            DateTimeOffset now = clock.GetUtcNow();
            if (item.Kind == "Locker")
            {
                profile.RegisteredLockerId = item.TargetLockerId;
                profile.UpdatedAt = now;
            }
            else
            {
                resident.PhoneNumber = item.NewPhoneNumber;
                resident.PhoneVerifiedAt = now;
                resident.UpdatedAt = now;
                foreach (var token in await db.RefreshTokens.Where(t => t.UserId == resident.Id && t.RevokedAt == null).ToListAsync(ct)) token.RevokedAt = now;
                foreach (var challenge in await db.OtpChallenges.Where(c => c.UserId == resident.Id && c.UsedAt == null && c.RevokedAt == null).ToListAsync(ct)) challenge.RevokedAt = now;
            }
            item.Status = "Applied";
            item.AppliedAt = now;
            AddEvent(item, actorId, "Applied", reason);
        }
        Notify(item);
        try { await SaveAsync(transaction, ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UX_User_PhoneNumber" })
        {
            throw new ConflictException("account-change/phone-unavailable", "The new phone number is already in use. No account change was applied.");
        }
        return await MapAsync(item, ct);
    }

    private async Task<List<AccountChangeBlocker>> LockerBlockersAsync(ResidentProfile profile, Guid targetId, CancellationToken ct)
    {
        var blockers = new List<AccountChangeBlocker>();
        DateTimeOffset now = clock.GetUtcNow();
        if (targetId == profile.RegisteredLockerId) blockers.Add(new("Locker", targetId, "Choose a different locker."));
        if (!await db.OperationalLockers().AsNoTracking().AnyAsync(l => l.Id == targetId, ct))
            blockers.Add(new("Locker", targetId, "Target locker is unavailable for registration."));
        blockers.AddRange(await FulfillmentBlockersAsync(profile, ct));
        var charges = await db.OverdueCharges.AsNoTracking().Where(c => c.Parcel.DeliveryRequest.ResidentProfileId == profile.Id && c.Status == OverdueChargeStatus.Outstanding && c.Amount > 0)
            .Select(c => c.ParcelId).ToListAsync(ct);
        blockers.AddRange(charges.Select(id => new AccountChangeBlocker("Parcel", id, "An overdue charge is outstanding.")));
        var payments = await db.PaymentTransactions.AsNoTracking().Where(p => p.OverdueCharge.Parcel.DeliveryRequest.ResidentProfileId == profile.Id && p.Status == PaymentTransactionStatus.Pending)
            .Select(p => p.Id).ToListAsync(ct);
        blockers.AddRange(payments.Select(id => new AccountChangeBlocker("OverduePayment", id, "Payment is pending or its provider result has not been confirmed.")));
        var subscriptions = await db.ResidentSubscriptions.AsNoTracking().Where(s => s.ResidentProfileId == profile.Id &&
            (s.Status == SubscriptionStatus.PendingPayment || (s.Status == SubscriptionStatus.Active && s.EndDate > now))).ToListAsync(ct);
        foreach (var s in subscriptions)
        {
            if (s.Status == SubscriptionStatus.PendingPayment) blockers.Add(new("Subscription", s.Id, "Subscription payment is pending or unclear."));
            else if (s.BenefitsSnapshotRecordedAt is null || s.PlanCodeSnapshot is null || s.PlanNameSnapshot is null || s.DurationDaysSnapshot is null || s.MaxActiveParcelsSnapshot is null)
                blockers.Add(new("Subscription", s.Id, "Purchased benefits need reconciliation before they can be preserved at the target locker."));
            // Plans are system-wide, with no locker restriction. Preserve every purchased snapshot and period unchanged.
        }
        var cases = await db.Set<PaymentReconciliation>().AsNoTracking().Where(c => c.ResidentUserId == profile.UserId && c.Status != "Closed" && c.Status != "Resolved")
            .Select(c => new { c.Id, c.Reference }).ToListAsync(ct);
        blockers.AddRange(cases.Select(c => new AccountChangeBlocker("Reconciliation", c.Id, $"Reconciliation {c.Reference} is unresolved.")));
        return blockers;
    }

    private async Task<List<AccountChangeBlocker>> FulfillmentBlockersAsync(ResidentProfile profile, CancellationToken ct)
    {
        var blockers = new List<AccountChangeBlocker>();
        var parcels = await db.Parcels.AsNoTracking().Where(p => p.DeliveryRequest.ResidentProfileId == profile.Id &&
            (p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue ||
             (p.Status == ParcelStatus.Removed && (p.RemovedAt == null || p.RemovedByUserId == null)))).Select(p => new { p.Id, p.ParcelCode }).ToListAsync(ct);
        blockers.AddRange(parcels.Select(p => new AccountChangeBlocker("Parcel", p.Id, $"Parcel {p.ParcelCode} must be collected or physically confirmed at the recovery address.")));
        var deliveries = await db.DeliveryRequests.AsNoTracking().Where(d => d.ResidentProfileId == profile.Id &&
            (d.Status == DeliveryRequestStatus.Started || d.Status == DeliveryRequestStatus.PendingApproval || d.Status == DeliveryRequestStatus.Approved ||
             d.Status == DeliveryRequestStatus.Allocated || (d.AllocatedCompartmentId != null && d.CompartmentReleasedAt == null &&
                (d.Parcel == null || d.Parcel.Status != ParcelStatus.Removed || d.Parcel.RemovedAt == null || d.Parcel.RemovedByUserId == null))))
            .Select(d => d.Id).ToListAsync(ct);
        blockers.AddRange(deliveries.Select(id => new AccountChangeBlocker("Delivery", id, "Delivery approval, allocation or compartment access has not finished.")));
        var returns = await db.ReturnRequests.AsNoTracking().Where(r => r.ResidentProfileId == profile.Id &&
            (r.Status == ReturnRequestStatus.Created || r.Status == ReturnRequestStatus.Allocated || r.Status == ReturnRequestStatus.Deposited ||
             (r.AllocatedCompartmentId != null && r.CompartmentReleasedAt == null) ||
             (r.Cancellation != null && r.Cancellation.Status == "PendingVerification"))).Select(r => r.Id).ToListAsync(ct);
        blockers.AddRange(returns.Select(id => new AccountChangeBlocker("Return", id, "Return delivery, pickup or compartment access has not finished.")));
        var reservations = await db.CompartmentReservations.AsNoTracking().Where(r => r.ReleasedAt == null &&
            ((r.DeliveryRequest != null && r.DeliveryRequest.ResidentProfileId == profile.Id) ||
             (r.ReturnRequest != null && r.ReturnRequest.ResidentProfileId == profile.Id))).Select(r => new { r.Id, r.DeliveryRequestId, r.ReturnRequestId }).ToListAsync(ct);
        blockers.AddRange(reservations.Select(r => new AccountChangeBlocker(r.ReturnRequestId.HasValue ? "Return" : "Delivery", r.ReturnRequestId ?? r.DeliveryRequestId ?? r.Id, "A compartment reservation or access session still needs release confirmation.")));
        return blockers;
    }

    private async Task<List<AccountChangeBlocker>> PhoneBlockersAsync(ResidentAccountChange item, User resident, CancellationToken ct)
    {
        var blockers = new List<AccountChangeBlocker>();
        if (item.PhoneVerificationExpiresAt is null || item.PhoneVerificationExpiresAt <= clock.GetUtcNow()) blockers.Add(new("PhoneVerification", item.Id, "New phone verification expired. Verify a new OTP and resubmit this request."));
        if (resident.PhoneNumber != item.OriginalPhoneNumber) blockers.Add(new("Account", item.Id, "The account phone changed after submission. Cancel and create a new request."));
        blockers.AddRange(await FulfillmentBlockersAsync(await ProfileAsync(resident.Id, ct), ct));
        if (await db.Users.AsNoTracking().AnyAsync(u => u.Id != resident.Id && u.PhoneNumber == item.NewPhoneNumber, ct))
            blockers.Add(new("PhoneVerification", item.Id, "The new phone number is already in use."));
        return blockers;
    }

    private async Task<IDbContextTransaction?> LockResidentAsync(Guid userId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"User\" WHERE \"Id\" = {userId} FOR UPDATE", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"ResidentProfile\" WHERE \"UserId\" = {userId} FOR UPDATE", ct);
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is User u && u.Id == userId || e.Entity is ResidentProfile r && r.UserId == userId).ToArray())
                await entry.ReloadAsync(ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private async Task<User> ActorAsync(Guid id, bool residentOnly, CancellationToken ct)
    {
        User user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException(UserErrorCodes.NotFound, "Account not found.");
        if (user.Status != UserStatus.Active || (residentOnly ? user.Role != UserRole.Resident : user.Role is not (UserRole.Resident or UserRole.Administrator)))
            throw new ForbiddenException("account-change/access-denied", "This account cannot access resident changes.");
        return user;
    }
    private Task<ResidentProfile> ProfileAsync(Guid id, CancellationToken ct) => db.ResidentProfiles.SingleAsync(r => r.UserId == id, ct);
    private IQueryable<ResidentAccountChange> Cases() => db.Set<ResidentAccountChange>().Include(c => c.Events).Include(c => c.TargetLocker);
    private async Task<ResidentAccountChange> CaseAsync(User actor, Guid id, CancellationToken ct) => await Cases()
        .SingleOrDefaultAsync(c => c.Id == id && (actor.Role == UserRole.Administrator || c.ResidentUserId == actor.Id), ct)
        ?? throw new NotFoundException("account-change/not-found", "Request not found.");
    private void VerifyPassword(User user, string password)
    {
        if (string.IsNullOrEmpty(password) || !passwords.VerifyPassword(password, user.PasswordHash))
            throw new UnauthorizedException(AuthErrorCodes.InvalidCredentials, "Current password is incorrect.");
    }
    private async Task EnsurePhoneAvailableAsync(User user, string phone, CancellationToken ct)
    {
        if (user.PhoneNumber == phone) throw BadRequestException.For("newPhoneNumber", "Choose a different phone number.");
        if (await db.Users.AnyAsync(u => u.Id != user.Id && u.PhoneNumber == phone, ct))
            throw new ConflictException("account-change/phone-unavailable", "The new phone number is already in use.");
    }
    private static string Phone(string value)
    {
        string phone = value?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(phone, "^0[0-9]{9}$")) throw BadRequestException.For("newPhoneNumber", "Phone number must contain 10 digits and start with 0.");
        return phone;
    }
    private static string Text(string value)
    {
        string text = value?.Trim() ?? string.Empty;
        if (text.Length is < 1 or > 2000) throw BadRequestException.For("reason", "Reason must contain between 1 and 2000 characters.");
        return text;
    }
    private ResidentAccountChange New(Guid userId, string kind, string reason) => new()
    {
        Id = Guid.NewGuid(),
        ResidentUserId = userId,
        Kind = kind,
        Reason = reason,
        Status = "Submitted",
        CreatedAt = clock.GetUtcNow(),
        UpdatedAt = clock.GetUtcNow()
    };
    private void AddEvent(ResidentAccountChange item, Guid actorId, string action, string message)
    {
        item.UpdatedAt = clock.GetUtcNow();
        item.Events.Add(new() { Id = Guid.NewGuid(), ActorUserId = actorId, Action = action, Message = message, CreatedAt = item.UpdatedAt });
        db.AuditLogs.Add(new()
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorId,
            Action = "ResidentAccountChange." + action,
            EntityType = nameof(ResidentAccountChange),
            EntityId = item.Id,
            Result = AuditLogResult.Succeeded,
            Details = item.Kind + ": " + item.Status,
            OccurredAt = item.UpdatedAt
        });
    }
    private void Notify(ResidentAccountChange item) => db.Notifications.Add(new()
    {
        Id = Guid.NewGuid(),
        UserId = item.ResidentUserId,
        Type = "ResidentAccountChange",
        Channel = NotificationChannel.InApp,
        Title = "Yêu cầu thay đổi tài khoản",
        Message = item.Status == "Applied" ? "Yêu cầu đã được áp dụng. Đổi số điện thoại yêu cầu đăng nhập lại bằng số mới." : "Yêu cầu đã bị từ chối. Mở lịch sử thay đổi tài khoản để xem lý do.",
        DeliveryStatus = NotificationDeliveryStatus.Sent,
        CreatedAt = clock.GetUtcNow(),
        SentAt = clock.GetUtcNow()
    });
    private async Task SaveAsync(IDbContextTransaction? transaction, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }
    private async Task<ResidentAccountChangeResponse> MapAsync(ResidentAccountChange item, CancellationToken ct, List<AccountChangeBlocker>? blockers = null)
    {
        if (blockers is null && item.Status == "Submitted")
            blockers = item.Kind == "Locker" ? await LockerBlockersAsync(await ProfileAsync(item.ResidentUserId, ct), item.TargetLockerId!.Value, ct)
                : await PhoneBlockersAsync(item, await db.Users.SingleAsync(u => u.Id == item.ResidentUserId, ct), ct);
        return new(item.Id, item.ResidentUserId, item.Kind, item.Status, item.Reason, item.OriginalLockerId, item.TargetLockerId,
            item.TargetLocker?.Code, item.OriginalPhoneNumber, item.NewPhoneNumber, item.PhoneVerificationExpiresAt, item.ReviewReason,
            item.CreatedAt, item.UpdatedAt, item.AppliedAt, blockers ?? [], item.Events.OrderBy(e => e.CreatedAt).Select(e => new AccountChangeEventResponse(e.Id, e.Action, e.Message, e.CreatedAt)).ToArray());
    }
}
