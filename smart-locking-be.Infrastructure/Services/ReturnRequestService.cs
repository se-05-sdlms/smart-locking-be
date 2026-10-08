using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Returns;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ReturnRequestService(
    ApplicationDbContext dbContext,
    ILockerAccessService lockerAccessService,
    ICompartmentAllocationService compartmentAllocationService,
    IPushNotificationService pushNotificationService,
    ITokenHashService tokenHashService,
    TimeProvider timeProvider) : IReturnRequestService, IReturnPickupSessionService
{
    public async Task<ReturnRequestResponse> CreateAsync(Guid residentUserId, CreateReturnRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl)) throw new ArgumentException("Ảnh kiện hàng trả là bắt buộc.");
        ResidentProfile resident = await dbContext.ResidentProfiles
            .Include(item => item.User).Include(item => item.RegisteredLocker)
            .SingleOrDefaultAsync(item => item.UserId == residentUserId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy hồ sơ cư dân.");
        if (resident.User.Status != UserStatus.Active) throw new InvalidOperationException("Tài khoản cư dân không hoạt động.");
        if (resident.RegisteredLocker is null || resident.RegisteredLocker.OperationalStatus != LockerOperationalStatus.Operational)
            throw new InvalidOperationException("Tủ đã đăng ký hiện không khả dụng.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        ReturnRequest entity = new()
        {
            Id = Guid.NewGuid(), ResidentProfileId = resident.Id, LockerId = resident.RegisteredLocker.Id,
            ReturnCode = await GeneratePickupCodeAsync(cancellationToken), ReturnReason = request.Note?.Trim(),
            ReturnImageUrl = request.ImageUrl.Trim(), Status = ReturnRequestStatus.Created, CreatedAt = now, UpdatedAt = now
        };
        dbContext.ReturnRequests.Add(entity);
        AddAudit(residentUserId, "ReturnRequest.Created", nameof(ReturnRequest), entity.Id, "Resident created a return request.", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        entity.Locker = resident.RegisteredLocker;
        return Map(entity);
    }

    public async Task<PagedResult<ReturnRequestResponse>> GetMineAsync(
        Guid residentUserId,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20)
    {
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        IQueryable<ReturnRequest> query = ResidentQuery(residentUserId);
        int totalCount = await query.CountAsync(cancellationToken);
        ReturnRequest[] entities = await query.OrderByDescending(item => item.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return new PagedResult<ReturnRequestResponse>(entities.Select(Map).ToArray(), totalCount, pageNumber, pageSize);
    }

    public async Task<ReturnRequestResponse> GetAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default) =>
        Map(await ResidentQuery(residentUserId).SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu gửi hàng trả."));

    public async Task<ReturnUnlockResponse> OpenCompartmentAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default)
    {
        ReturnRequest entity = await ResidentQuery(residentUserId).SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu gửi hàng trả.");
        if (entity.Status != ReturnRequestStatus.Created) throw new InvalidOperationException("Yêu cầu không ở trạng thái có thể cấp ngăn.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        DateTimeOffset expiresAt = now.AddMinutes(policy.CompartmentReservationMinutes);
        CompartmentReservation? reservation = await compartmentAllocationService.ReserveAvailableAsync(
            entity.LockerId,
            null,
            entity.Id,
            expiresAt,
            cancellationToken);
        if (reservation is null) throw new InvalidOperationException("Tủ hiện không còn ngăn trống.");
        LockerCompartment compartment = reservation.LockerCompartment;
        entity.AllocatedCompartmentId = compartment.Id;
        entity.AllocatedAt = now;
        entity.ReservationExpiresAt = expiresAt;
        entity.Status = ReturnRequestStatus.Allocated;
        entity.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        OpenLockerResponse opened = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            entity.LockerId, compartment.Id, residentUserId, null, null, entity.Id,
            LockerAccessType.ResidentReturnDropOff, LockerAccessMethod.RemoteApp, null, "Resident Mobile"), cancellationToken);
        if (opened.Result != LockerAccessResult.Succeeded)
        {
            await compartmentAllocationService.ReleaseAsync(null, entity.Id, now, cancellationToken);
            entity.Status = ReturnRequestStatus.Failed;
            entity.FailureReason = opened.FailureReason ?? "Không thể mở ngăn tủ.";
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(entity.FailureReason);
        }
        AddAudit(residentUserId, "ReturnRequest.CompartmentOpened", nameof(ReturnRequest), entity.Id, compartment.Code, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ReturnUnlockResponse(entity.Id, compartment.Code, opened.AccessEventId, expiresAt);
    }

    public async Task FinalizeDepositAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        ReturnRequest entity = await FullQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu gửi hàng trả.");
        if (entity.Status == ReturnRequestStatus.Deposited) return;
        if (entity.Status != ReturnRequestStatus.Allocated || entity.AllocatedCompartment is null)
            throw new InvalidOperationException("Yêu cầu chưa được cấp ngăn.");
        DateTimeOffset now = completedAt;
        entity.Status = ReturnRequestStatus.Deposited;
        entity.ResidentDepositedAt = now;
        entity.UpdatedAt = now;
        await compartmentAllocationService.ReleaseAsync(null, entity.Id, now, cancellationToken);
        Guid pushId = pushNotificationService.EnqueueReturnNotification(
            entity.ResidentProfile.UserId, entity.Id, "ReturnDeposited", "Hàng gửi đã được lưu",
            $"Kiện hàng trả đã được lưu tại ngăn {entity.AllocatedCompartment.Code}. Mã shipper lấy hàng: {entity.ReturnCode}.");
        AddAudit(entity.ResidentProfile.UserId, "ReturnRequest.Deposited", nameof(ReturnRequest), entity.Id, entity.AllocatedCompartment.Code, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await pushNotificationService.TrySendAsync(pushId, cancellationToken);
    }

    public async Task<ReturnPickupSessionResponse> CreateAsync(ValidateReturnPickupRequest request, CancellationToken cancellationToken = default)
    {
        string code = request.PickupCode.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[0-9]{6}$")) throw new ArgumentException("Mã lấy hàng phải gồm đúng 6 chữ số.");
        ReturnRequest entity = await FullQuery().SingleOrDefaultAsync(item => item.ReturnCode == code && item.Locker.Code == request.LockerCode.Trim(), cancellationToken)
            ?? throw new KeyNotFoundException("Mã lấy hàng không hợp lệ tại tủ này.");
        if (entity.Status != ReturnRequestStatus.Deposited || entity.AllocatedCompartment is null)
            throw new InvalidOperationException("Mã lấy hàng đã dùng hoặc chưa sẵn sàng.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        string token = tokenHashService.CreateSecureToken();
        entity.ShipperSessionTokenHash = tokenHashService.HashToken(token);
        entity.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ReturnPickupSessionResponse(entity.Id, token, entity.Locker.Code, entity.AllocatedCompartment.Code, entity.ReturnImageUrl!, now.AddMinutes(policy.GuestSessionTimeoutMinutes));
    }

    public async Task<ReturnPickupSessionResponse> GetAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default)
    {
        ReturnRequest entity = await GetValidGuestReturnAsync(id, guestSessionToken, cancellationToken);
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ReturnPickupSessionResponse(entity.Id, guestSessionToken, entity.Locker.Code, entity.AllocatedCompartment!.Code, entity.ReturnImageUrl!, entity.UpdatedAt.AddMinutes(policy.GuestSessionTimeoutMinutes));
    }

    public async Task<ReturnPickupSessionResponse> OpenCompartmentAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default)
    {
        ReturnRequest entity = await GetValidGuestReturnAsync(id, guestSessionToken, cancellationToken);
        OpenLockerResponse opened = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            entity.LockerId, entity.AllocatedCompartmentId!.Value, null, null, null, entity.Id,
            LockerAccessType.ShipperReturnPickup, LockerAccessMethod.GuestSession, null, "Shipper Guest Web"), cancellationToken);
        if (opened.Result != LockerAccessResult.Succeeded) throw new InvalidOperationException(opened.FailureReason ?? "Không thể mở ngăn tủ.");
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        return new ReturnPickupSessionResponse(entity.Id, guestSessionToken, entity.Locker.Code, entity.AllocatedCompartment!.Code, entity.ReturnImageUrl!, timeProvider.GetUtcNow().AddMinutes(policy.GuestSessionTimeoutMinutes));
    }

    public async Task FinalizePickupAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        ReturnRequest entity = await FullQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu gửi hàng trả.");
        if (entity.Status == ReturnRequestStatus.Completed) return;
        if (entity.Status != ReturnRequestStatus.Deposited || entity.AllocatedCompartment is null)
            throw new InvalidOperationException("Yêu cầu không ở trạng thái có thể hoàn tất lấy hàng.");
        DateTimeOffset now = completedAt;
        entity.Status = ReturnRequestStatus.Completed;
        entity.ShipperPickedUpAt = now;
        entity.CompartmentReleasedAt = now;
        entity.ShipperSessionTokenHash = null;
        entity.UpdatedAt = now;
        Guid pushId = pushNotificationService.EnqueueReturnNotification(
            entity.ResidentProfile.UserId, entity.Id, "ReturnPickedUp", "Shipper đã lấy hàng gửi",
            $"Shipper đã lấy kiện hàng gửi tại tủ {entity.Locker.Code}, ngăn {entity.AllocatedCompartment.Code}.");
        AddAudit(null, "ReturnRequest.PickedUp", nameof(ReturnRequest), entity.Id, entity.AllocatedCompartment.Code, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await pushNotificationService.TrySendAsync(pushId, cancellationToken);
    }

    public async Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<ReturnRequest> items = await dbContext.ReturnRequests
            .Where(item => item.Status == ReturnRequestStatus.Allocated && item.ReservationExpiresAt <= now)
            .ToListAsync(cancellationToken);
        foreach (ReturnRequest item in items)
        {
            item.Status = ReturnRequestStatus.Expired; item.UpdatedAt = now; item.CompartmentReleasedAt = now;
            await compartmentAllocationService.ReleaseAsync(null, item.Id, now, cancellationToken);
        }
        if (items.Count > 0) await dbContext.SaveChangesAsync(cancellationToken);
        return items.Count;
    }

    private IQueryable<ReturnRequest> ResidentQuery(Guid userId) => FullQuery().Where(item => item.ResidentProfile.UserId == userId);
    private IQueryable<ReturnRequest> FullQuery() => dbContext.ReturnRequests
        .Include(item => item.ResidentProfile).ThenInclude(profile => profile.User)
        .Include(item => item.Locker).Include(item => item.AllocatedCompartment);

    private async Task<ReturnRequest> GetValidGuestReturnAsync(Guid id, string token, CancellationToken cancellationToken)
    {
        ReturnRequest entity = await FullQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu gửi hàng trả.");
        if (entity.Status != ReturnRequestStatus.Deposited || string.IsNullOrWhiteSpace(entity.ShipperSessionTokenHash))
            throw new InvalidOperationException("Phiên lấy hàng không còn hiệu lực.");
        if (!string.Equals(entity.ShipperSessionTokenHash, tokenHashService.HashToken(token.Trim()), StringComparison.Ordinal))
            throw new System.Security.Authentication.AuthenticationException("Guest session token không hợp lệ.");
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        if (entity.UpdatedAt.AddMinutes(policy.GuestSessionTimeoutMinutes) <= timeProvider.GetUtcNow()) throw new TimeoutException("Phiên lấy hàng đã hết hạn.");
        entity.UpdatedAt = timeProvider.GetUtcNow();
        return entity;
    }

    private async Task<SystemPolicy> ActivePolicyAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return await dbContext.SystemPolicies
            .Where(item => item.IsActive && item.EffectiveFrom <= now && (!item.EffectiveTo.HasValue || item.EffectiveTo > now))
            .OrderByDescending(item => item.EffectiveFrom)
            .FirstAsync(cancellationToken);
    }

    private async Task<string> GeneratePickupCodeAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            if (!await dbContext.ReturnRequests.AnyAsync(item => item.ReturnCode == code && item.Status != ReturnRequestStatus.Completed, cancellationToken)) return code;
        }
        throw new InvalidOperationException("Không thể tạo mã lấy hàng. Vui lòng thử lại.");
    }

    private void AddAudit(Guid? actor, string action, string entityType, Guid entityId, string details, DateTimeOffset now) =>
        dbContext.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorUserId = actor, Action = action, EntityType = entityType, EntityId = entityId, Result = AuditLogResult.Succeeded, Details = details, OccurredAt = now });

    private static ReturnRequestResponse Map(ReturnRequest item) => new(
        item.Id, item.Status == ReturnRequestStatus.Created || item.Status == ReturnRequestStatus.Allocated ? string.Empty : item.ReturnCode,
        item.ReturnImageUrl!, item.ReturnReason, item.Status, item.Locker.Code, item.Locker.Address,
        item.AllocatedCompartment?.Code, item.CreatedAt, item.ReservationExpiresAt, item.ResidentDepositedAt, item.ShipperPickedUpAt);
}
