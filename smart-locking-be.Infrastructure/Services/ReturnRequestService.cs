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
    TimeProvider timeProvider) : IReturnRequestService
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
            Id = Guid.NewGuid(),
            ResidentProfileId = resident.Id,
            LockerId = resident.RegisteredLocker.Id,
            ReturnCode = await GeneratePickupCodeAsync(cancellationToken),
            ReturnReason = request.Note?.Trim(),
            ReturnImageUrl = request.ImageUrl.Trim(),
            Status = ReturnRequestStatus.Created,
            CreatedAt = now,
            UpdatedAt = now
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

    private IQueryable<ReturnRequest> ResidentQuery(Guid userId) => FullQuery().Where(item => item.ResidentProfile.UserId == userId);
    private IQueryable<ReturnRequest> FullQuery() => dbContext.ReturnRequests
        .Include(item => item.ResidentProfile).ThenInclude(profile => profile.User)
        .Include(item => item.Locker).Include(item => item.AllocatedCompartment);

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
