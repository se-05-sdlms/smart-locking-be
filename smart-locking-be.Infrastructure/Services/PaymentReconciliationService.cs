using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class PaymentReconciliationService(ApplicationDbContext db, IPaymentGatewayService gateway,
    IResidentSubscriptionService subscriptions, IOverduePaymentService overduePayments, TimeProvider clock) : IPaymentReconciliationService
{
    public async Task<PagedResult<PaymentLookupResponse>> GetPaymentsAsync(Guid actorId, PaymentLookupQuery query, CancellationToken cancellationToken = default)
    {
        User actor = await ActorAsync(actorId, false, cancellationToken);
        if (query.Kind is not null) ValidateKind(query.Kind);
        if (query.From > query.To) throw BadRequestException.For("to", "End date must follow start date.");
        Guid? owner = actor.Role == UserRole.Administrator ? query.ResidentUserId : actorId;
        var rows = db.ResidentSubscriptions.AsNoTracking().Where(s => s.ExternalOrderCode != null)
            .Select(s => new
            {
                Kind = "Subscription",
                s.Id,
                ResidentUserId = s.ResidentProfile.UserId,
                OrderCode = s.ExternalOrderCode,
                Amount = s.PriceSnapshot,
                Currency = "VND",
                Status = s.PaidAt != null ? "Success" : s.Status == SubscriptionStatus.Cancelled ? "Cancelled" : "Pending",
                s.CreatedAt,
                CompletedAt = s.PaidAt,
                s.ExternalTransactionId
            })
            .Concat(db.PaymentTransactions.AsNoTracking().Select(p => new
            {
                Kind = "Overdue",
                p.Id,
                ResidentUserId = p.OverdueCharge.Parcel.DeliveryRequest.ResidentProfile!.UserId,
                OrderCode = p.ExternalOrderCode,
                p.Amount,
                p.Currency,
                Status = p.Status == PaymentTransactionStatus.Success ? "Success" : p.Status == PaymentTransactionStatus.Cancelled ? "Cancelled" : p.Status == PaymentTransactionStatus.Failed ? "Failed" : "Pending",
                CreatedAt = p.RequestedAt,
                p.CompletedAt,
                p.ExternalTransactionId
            }));
        if (owner.HasValue) rows = rows.Where(p => p.ResidentUserId == owner);
        if (query.Kind is not null) rows = rows.Where(p => p.Kind == query.Kind);
        if (!string.IsNullOrWhiteSpace(query.OrderCode)) rows = rows.Where(p => p.OrderCode == query.OrderCode || p.ExternalTransactionId == query.OrderCode);
        if (query.From.HasValue) rows = rows.Where(p => p.CreatedAt >= query.From);
        if (query.To.HasValue) rows = rows.Where(p => p.CreatedAt <= query.To);
        if (!string.IsNullOrWhiteSpace(query.Status)) rows = rows.Where(p => p.Status == query.Status);
        int page = Math.Max(1, query.PageNumber), size = Math.Clamp(query.PageSize, 1, 100);
        int count = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id).Skip((page - 1) * size).Take(size)
            .Select(p => new PaymentLookupResponse(p.Kind, p.Id, p.ResidentUserId, p.OrderCode, p.Amount, p.Currency,
                p.Status, p.CreatedAt, p.CompletedAt, p.ExternalTransactionId, null)).ToListAsync(cancellationToken);
        return new(items, count, page, size);
    }

    public async Task<PaymentLookupResponse> GetPaymentAsync(Guid actorId, string kind, Guid paymentId, CancellationToken cancellationToken = default)
    {
        User actor = await ActorAsync(actorId, false, cancellationToken);
        return await PaymentAsync(actor, kind, paymentId, cancellationToken);
    }

    public async Task<PagedResult<PaymentReconciliationResponse>> GetCasesAsync(Guid actorId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        User actor = await ActorAsync(actorId, false, cancellationToken);
        var query = Cases().AsNoTracking().Where(r => actor.Role == UserRole.Administrator || r.ResidentUserId == actorId);
        int page = Math.Max(1, pageNumber), size = Math.Clamp(pageSize, 1, 100);
        int count = await query.CountAsync(cancellationToken);
        var cases = await query.OrderByDescending(r => r.UpdatedAt).ThenBy(r => r.Id).Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new(cases.Select(Map).ToArray(), count, page, size);
    }

    public async Task<PaymentReconciliationResponse> GetCaseAsync(Guid actorId, Guid id, CancellationToken cancellationToken = default)
    {
        User actor = await ActorAsync(actorId, false, cancellationToken);
        return Map(await CaseAsync(actor, id, true, cancellationToken));
    }

    public async Task<PaymentReconciliationResponse> CreateAsync(Guid actorId, CreatePaymentReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        User actor = await ActorAsync(actorId, false, cancellationToken);
        string reason = RequireText(request.Reason, "reason");
        PaymentLookupResponse payment = await PaymentAsync(actor, request.Kind, request.PaymentId, cancellationToken);
        if (payment.Amount <= 0 || string.IsNullOrWhiteSpace(payment.OrderCode))
            throw BadRequestException.For("paymentId", "This record has no payment order to reconcile.");
        // All retries for this exact purchase/payment return the same case and reference.
        await using var transaction = await LockPaymentAsync(payment, cancellationToken);
        PaymentReconciliation? existing = await Cases().SingleOrDefaultAsync(r => request.Kind == "Subscription"
            ? r.SubscriptionId == request.PaymentId : r.PaymentTransactionId == request.PaymentId, cancellationToken);
        if (existing is not null) return Map(existing);
        DateTimeOffset now = clock.GetUtcNow();
        Guid id = Guid.NewGuid();
        var item = new PaymentReconciliation
        {
            Id = id,
            Reference = $"REC-{id:N}",
            ResidentUserId = payment.ResidentUserId,
            SubscriptionId = request.Kind == "Subscription" ? request.PaymentId : null,
            PaymentTransactionId = request.Kind == "Overdue" ? request.PaymentId : null,
            Reason = reason,
            Status = "Submitted",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Set<PaymentReconciliation>().Add(item);
        AddEvent(item, actorId, "Submitted", "Request received. Keep this reference and do not pay again while the result is unclear.");
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Map(item);
    }

    public async Task<PaymentReconciliationResponse> AssignAsync(Guid actorId, Guid id, CancellationToken cancellationToken = default)
    {
        User actor = await ActorAsync(actorId, true, cancellationToken);
        await using var transaction = await LockCaseAsync(id, cancellationToken);
        PaymentReconciliation item = await CaseAsync(actor, id, false, cancellationToken);
        if (item.AssignedAdminUserId == actorId && item.Status == "InReview") return Map(item);
        item.AssignedAdminUserId = actorId;
        item.Status = "InReview";
        AddEvent(item, actorId, "Assigned", "An administrator has accepted this reconciliation.");
        await SaveCaseAsync(item, transaction, cancellationToken);
        return Map(item);
    }

    public async Task<PaymentReconciliationResponse> RespondAsync(Guid actorId, Guid id, RespondPaymentReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        User actor = await ActorAsync(actorId, true, cancellationToken);
        if (request.Status is not ("InReview" or "WaitingProvider" or "Closed"))
            throw BadRequestException.For("status", "Manual responses can only set InReview, WaitingProvider or Closed; payment success requires verified gateway data.");
        string response = RequireText(request.Response, "response");
        await using var transaction = await LockCaseAsync(id, cancellationToken);
        PaymentReconciliation item = await CaseAsync(actor, id, false, cancellationToken);
        item.AssignedAdminUserId ??= actorId;
        item.Status = request.Status;
        item.Response = response;
        AddEvent(item, actorId, "Responded", response);
        Notify(item, "An administrator responded to your payment reconciliation.");
        await SaveCaseAsync(item, transaction, cancellationToken);
        return Map(item);
    }

    public async Task<PaymentReconciliationResponse> SynchronizeAsync(Guid actorId, Guid id, CancellationToken cancellationToken = default)
    {
        User actor = await ActorAsync(actorId, true, cancellationToken);
        PaymentReconciliation original = await CaseAsync(actor, id, true, cancellationToken);
        string kind = original.SubscriptionId.HasValue ? "Subscription" : "Overdue";
        Guid paymentId = original.SubscriptionId ?? original.PaymentTransactionId!.Value;
        PaymentLookupResponse payment = await PaymentAsync(actor, kind, paymentId, cancellationToken);
        string status = "WaitingProvider", message = "Payment result is unclear. Await provider verification; do not pay again.", evidence = "";
        PaymentGatewayInformation? information = null;
        try
        {
            if (!long.TryParse(payment.OrderCode, CultureInfo.InvariantCulture, out long orderCode) || orderCode <= 0)
                throw new ConflictException(PaymentErrorCodes.InvalidRequest, "The payment has no valid gateway order code.");
            information = await gateway.GetPaymentLinkInformationAsync(orderCode, cancellationToken);
            evidence = JsonSerializer.Serialize(information);
            bool matching = information.OrderCode == orderCode && information.Amount == payment.Amount && payment.Currency == "VND";
            bool verifiedPaid = matching && information.Status == "PAID" && information.AmountPaid == payment.Amount && information.AmountRemaining == 0 &&
                information.Transactions.Count > 0 && information.Transactions.All(t => t.Amount > 0 && !string.IsNullOrWhiteSpace(t.Reference) && t.Reference.Length <= 150) &&
                information.Transactions.Sum(t => (long)t.Amount) == information.AmountPaid;
            if (verifiedPaid)
            {
                var confirmed = new PayOSWebhookData(orderCode, checked((int)payment.Amount), information.Transactions[^1].Reference, "00", "VND");
                if (kind == "Subscription") await subscriptions.ActivateSubscriptionAsync(confirmed, cancellationToken);
                else await overduePayments.TryCompleteAsync(confirmed, cancellationToken);
                PaymentLookupResponse current = await PaymentAsync(actor, kind, paymentId, cancellationToken);
                bool settled = current.Status == "Success" && (kind == "Subscription" ||
                    await db.PaymentTransactions.AsNoTracking().AnyAsync(row => row.Id == paymentId &&
                        row.OverdueCharge.Status == OverdueChargeStatus.Paid && row.FailureReason == null, cancellationToken));
                status = settled ? "Resolved" : "Conflict";
                message = settled ? "Gateway payment verified and recorded. Benefits were applied once."
                    : "The gateway confirms payment but the internal record conflicts. An administrator must investigate; do not pay again.";
            }
            else if (matching && information.Status is "CANCELLED" or "EXPIRED" && information.AmountPaid == 0 && information.Transactions.Count == 0)
            {
                await ApplyUnpaidTerminationAsync(payment, cancellationToken);
                PaymentLookupResponse current = await PaymentAsync(actor, kind, paymentId, cancellationToken);
                status = current.Status == "Cancelled" ? "Resolved" : "Conflict";
                message = current.Status == "Cancelled" ? "The gateway confirms this order ended without payment. This order was not charged. Contact support if your bank reports a debit."
                    : "The gateway and internal payment results conflict. Keep your reference and await administrator review.";
            }
            else if (!matching || information.Status == "PAID" || payment.Status == "Success")
            {
                status = "Conflict";
                message = "Payment amounts or results conflict. No payment state was overwritten; an administrator must investigate. Do not pay again.";
            }
        }
        catch (ServiceUnavailableException)
        {
            message = "The gateway could not be reached. Your reconciliation remains open; do not pay again while the payment result is unclear.";
        }
        catch (ApiException)
        {
            status = "Conflict";
            message = "Verified payment data could not be applied safely. An administrator must investigate; do not pay again.";
        }

        // Gateway calls and activation own their transactions. Acquire the case lock only afterwards.
        await using var transaction = await LockCaseAsync(id, cancellationToken);
        PaymentReconciliation item = await CaseAsync(actor, id, false, cancellationToken);
        item.AssignedAdminUserId ??= actorId;
        item.Status = status;
        item.Response = message;
        item.ProviderStatus = information?.Status;
        item.ProviderCheckedAt = clock.GetUtcNow();
        AddEvent(item, actorId, "ProviderChecked", message, evidence);
        Notify(item, message);
        await SaveCaseAsync(item, transaction, cancellationToken);
        return Map(item);
    }

    private async Task ApplyUnpaidTerminationAsync(PaymentLookupResponse payment, CancellationToken cancellationToken)
    {
        await using var transaction = await LockPaymentAsync(payment, cancellationToken);
        if (payment.Kind == "Subscription")
        {
            var row = await db.ResidentSubscriptions.SingleAsync(s => s.Id == payment.Id, cancellationToken);
            if (db.Database.IsRelational()) await db.Entry(row).ReloadAsync(cancellationToken);
            if (row.PaidAt is null && row.Status == SubscriptionStatus.PendingPayment)
            { row.Status = SubscriptionStatus.Cancelled; row.UpdatedAt = clock.GetUtcNow(); }
        }
        else
        {
            var row = await db.PaymentTransactions.Include(p => p.OverdueCharge).ThenInclude(charge => charge.Parcel)
                .SingleAsync(p => p.Id == payment.Id, cancellationToken);
            if (db.Database.IsRelational())
            {
                await db.Entry(row).ReloadAsync(cancellationToken);
                await db.Entry(row.OverdueCharge).ReloadAsync(cancellationToken);
            }
            if (row.Status == PaymentTransactionStatus.Pending)
            {
                DateTimeOffset now = clock.GetUtcNow();
                row.Status = PaymentTransactionStatus.Cancelled;
                row.CompletedAt = now;
                row.FailureReason = "Gateway confirmed this order ended without payment.";
                // Match checkout cancellation semantics before releasing the shared charge lock.
                // Never change the amount frozen by a different live replacement order.
                if (row.OverdueCharge.Status == OverdueChargeStatus.Outstanding &&
                    !await db.PaymentTransactions.AnyAsync(other => other.OverdueChargeId == row.OverdueChargeId &&
                        other.Id != row.Id && other.Status == PaymentTransactionStatus.Pending, cancellationToken))
                {
                    OverdueCharge charge = row.OverdueCharge;
                    DateTimeOffset cutoff = now > charge.Parcel.MaxStorageUntil ? charge.Parcel.MaxStorageUntil : now;
                    cutoff = cutoff < charge.ChargeStartAt ? charge.ChargeStartAt : cutoff;
                    int billableHours = (int)Math.Ceiling((cutoff - charge.ChargeStartAt).TotalHours);
                    charge.Amount = billableHours * charge.RatePerHourSnapshot;
                    charge.CalculatedThrough = cutoff;
                    charge.UpdatedAt = now;
                }
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private async Task<User> ActorAsync(Guid actorId, bool adminOnly, CancellationToken cancellationToken)
    {
        User actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actorId && u.Status == UserStatus.Active, cancellationToken)
            ?? throw new ForbiddenException(PaymentErrorCodes.InvalidRequest, "Active account required.");
        if (actor.Role != UserRole.Administrator && (adminOnly || actor.Role != UserRole.Resident))
            throw new ForbiddenException(PaymentErrorCodes.InvalidRequest, "This payment operation is not allowed.");
        return actor;
    }

    private async Task<PaymentLookupResponse> PaymentAsync(User actor, string kind, Guid id, CancellationToken cancellationToken)
    {
        ValidateKind(kind);
        PaymentLookupResponse? payment;
        if (kind == "Subscription")
            payment = await db.ResidentSubscriptions.AsNoTracking()
                .Where(s => s.Id == id && (actor.Role == UserRole.Administrator || s.ResidentProfile.UserId == actor.Id))
                .Select(s => new PaymentLookupResponse(kind, s.Id, s.ResidentProfile.UserId, s.ExternalOrderCode, s.PriceSnapshot, "VND",
                    s.PaidAt != null ? "Success" : s.Status == SubscriptionStatus.Cancelled ? "Cancelled" : "Pending", s.CreatedAt, s.PaidAt, s.ExternalTransactionId, null))
                .SingleOrDefaultAsync(cancellationToken);
        else
            payment = await db.PaymentTransactions.AsNoTracking()
                .Where(p => p.Id == id && (actor.Role == UserRole.Administrator || p.OverdueCharge.Parcel.DeliveryRequest.ResidentProfile!.UserId == actor.Id))
                .Select(p => new PaymentLookupResponse(kind, p.Id, p.OverdueCharge.Parcel.DeliveryRequest.ResidentProfile!.UserId, p.ExternalOrderCode, p.Amount, p.Currency,
                    p.Status == PaymentTransactionStatus.Success ? "Success" : p.Status == PaymentTransactionStatus.Cancelled ? "Cancelled" : p.Status == PaymentTransactionStatus.Failed ? "Failed" : "Pending",
                    p.RequestedAt, p.CompletedAt, p.ExternalTransactionId, null)).SingleOrDefaultAsync(cancellationToken);
        if (payment is null) throw new NotFoundException(PaymentErrorCodes.NotFound, "Payment not found.");
        Guid? caseId = await db.Set<PaymentReconciliation>().AsNoTracking().Where(r => kind == "Subscription" ? r.SubscriptionId == id : r.PaymentTransactionId == id)
            .Select(r => (Guid?)r.Id).SingleOrDefaultAsync(cancellationToken);
        return payment with { ReconciliationId = caseId };
    }

    private IQueryable<PaymentReconciliation> Cases() => db.Set<PaymentReconciliation>().Include(r => r.Events);
    private async Task<PaymentReconciliation> CaseAsync(User actor, Guid id, bool noTracking, CancellationToken cancellationToken)
    {
        var query = Cases();
        if (noTracking) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(r => r.Id == id && (actor.Role == UserRole.Administrator || r.ResidentUserId == actor.Id), cancellationToken)
            ?? throw new NotFoundException(PaymentErrorCodes.NotFound, "Payment reconciliation not found.");
    }

    private Task<IDbContextTransaction?> LockCaseAsync(Guid id, CancellationToken cancellationToken) => LockAsync("case", id, cancellationToken);
    private async Task<IDbContextTransaction?> LockPaymentAsync(PaymentLookupResponse payment, CancellationToken cancellationToken)
    {
        // Use the same resident/charge lock as webhook activation and checkout creation.
        Guid lockId = payment.Kind == "Subscription"
            ? await db.ResidentProfiles.Where(r => r.UserId == payment.ResidentUserId).Select(r => r.Id).SingleAsync(cancellationToken)
            : await db.PaymentTransactions.Where(p => p.Id == payment.Id).Select(p => p.OverdueChargeId).SingleAsync(cancellationToken);
        return await LockAsync(payment.Kind, lockId, cancellationToken);
    }
    private async Task<IDbContextTransaction?> LockAsync(string kind, Guid id, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational()) return null;
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (kind == "case") await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PaymentReconciliation\" WHERE \"Id\" = {id} FOR UPDATE", cancellationToken);
            else if (kind == "Subscription") await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"ResidentProfile\" WHERE \"Id\" = {id} FOR UPDATE", cancellationToken);
            else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"OverdueCharge\" WHERE \"Id\" = {id} FOR UPDATE", cancellationToken);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private void AddEvent(PaymentReconciliation item, Guid actorId, string action, string message, string? evidence = null)
    {
        item.UpdatedAt = clock.GetUtcNow();
        item.Events.Add(new PaymentReconciliationEvent
        {
            Id = Guid.NewGuid(),
            PaymentReconciliationId = item.Id,
            ActorUserId = actorId,
            Action = action,
            Status = item.Status,
            Message = message,
            ProviderEvidence = evidence,
            CreatedAt = item.UpdatedAt
        });
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorId,
            Action = $"PaymentReconciliation.{action}",
            EntityType = nameof(PaymentReconciliation),
            EntityId = item.Id,
            Result = AuditLogResult.Succeeded,
            Details = $"{item.Reference}: {item.Status}. {message}",
            OccurredAt = item.UpdatedAt
        });
    }
    private void Notify(PaymentReconciliation item, string message) => db.Notifications.Add(new Notification
    {
        Id = Guid.NewGuid(),
        UserId = item.ResidentUserId,
        Type = "PaymentReconciliationUpdated",
        Channel = NotificationChannel.InApp,
        Title = $"Reconciliation {item.Reference}",
        Message = message,
        DeliveryStatus = NotificationDeliveryStatus.Sent,
        CreatedAt = clock.GetUtcNow(),
        SentAt = clock.GetUtcNow()
    });
    private async Task SaveCaseAsync(PaymentReconciliation item, IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }
    private static void ValidateKind(string kind)
    {
        if (kind is not ("Subscription" or "Overdue")) throw BadRequestException.For("kind", "Kind must be Subscription or Overdue.");
    }
    private static string RequireText(string value, string field) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 2000
        ? value.Trim() : throw BadRequestException.For(field, "Enter between 1 and 2000 characters.");
    private static PaymentReconciliationResponse Map(PaymentReconciliation item) => new(item.Id, item.Reference,
        item.SubscriptionId.HasValue ? "Subscription" : "Overdue", item.SubscriptionId ?? item.PaymentTransactionId!.Value,
        item.Reason, item.Status, item.AssignedAdminUserId, item.Response, item.ProviderStatus, item.ProviderCheckedAt,
        item.CreatedAt, item.UpdatedAt, item.Response ?? "Your request is recorded. Keep your reference and do not pay again until the payment result is verified.",
        item.Events.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).Select(e => new PaymentReconciliationEventResponse(e.Id,
            e.Action, e.Status, e.Message, e.CreatedAt, e.ActorUserId, e.ProviderEvidence)).ToArray());
}
