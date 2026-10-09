using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class SystemPolicyService(ApplicationDbContext dbContext, TimeProvider timeProvider) : ISystemPolicyService
{
    public async Task<PagedResult<SystemPolicyResponse>> GetAsync(
        bool? isActive, int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        IQueryable<SystemPolicy> query = dbContext.SystemPolicies.AsNoTracking();
        if (isActive.HasValue) query = query.Where(item => item.IsActive == isActive);
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        int count = await query.CountAsync(cancellationToken);
        List<SystemPolicy> items = await query.OrderByDescending(item => item.Version)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<SystemPolicyResponse>(items.Select(Map).ToList(), count, pageNumber, pageSize);
    }

    public async Task<SystemPolicyResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Map(await dbContext.SystemPolicies.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("System policy not found."));

    public async Task<SystemPolicyResponse> CreateAsync(
        Guid adminUserId, SaveSystemPolicyRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        int version = (await dbContext.SystemPolicies.MaxAsync(item => (int?)item.Version, cancellationToken) ?? 0) + 1;
        DateTimeOffset now = timeProvider.GetUtcNow();
        SystemPolicy policy = new() { Id = Guid.NewGuid(), Version = version, CreatedByUserId = adminUserId, CreatedAt = now };
        Apply(policy, request);
        dbContext.SystemPolicies.Add(policy);
        AddAudit(adminUserId, "SystemPolicy.Created", policy.Id, $"Version={version}", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(policy);
    }

    public async Task<SystemPolicyResponse> UpdateAsync(
        Guid adminUserId, Guid id, SaveSystemPolicyRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        SystemPolicy policy = await dbContext.SystemPolicies.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("System policy not found.");
        if (policy.IsActive) throw new InvalidOperationException("Active policy is immutable. Create a new version instead.");
        Apply(policy, request);
        AddAudit(adminUserId, "SystemPolicy.Updated", policy.Id, $"Version={policy.Version}", timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(policy);
    }

    public async Task<SystemPolicyResponse> ActivateAsync(
        Guid adminUserId, Guid id, CancellationToken cancellationToken = default)
    {
        await using IDbContextTransaction? transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        SystemPolicy target = await dbContext.SystemPolicies.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("System policy not found.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<SystemPolicy> active = await dbContext.SystemPolicies.Where(item => item.IsActive && item.Id != id).ToListAsync(cancellationToken);
        foreach (SystemPolicy policy in active)
        {
            policy.IsActive = false;
            policy.EffectiveTo = now;
        }
        target.IsActive = true;
        target.EffectiveFrom = now;
        target.EffectiveTo = null;
        AddAudit(adminUserId, "SystemPolicy.Activated", target.Id, $"Version={target.Version}", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Map(target);
    }

    private static void Validate(SaveSystemPolicyRequest request)
    {
        if (request.GuestSessionTimeoutMinutes <= 0 || request.ManualApprovalTimeoutMinutes <= 0 || request.CompartmentReservationMinutes <= 0)
            throw new ArgumentException("Timeout values must be positive.");
        if (request.OverdueStartAfterHours != 7 * 24 || request.MaxStorageHours != 14 * 24)
            throw new ArgumentException("Storage timeline must be 7 normal days followed by 7 overdue days.");
        if (request.PickupReminderStartDay <= 0 || request.PickupReminderStartDay > Math.Ceiling(request.MaxStorageHours / 24d))
            throw new ArgumentException("Pickup reminder start day is outside the storage period.");
        if (request.OverdueFeePerHour < 0 || request.Currency.Trim().Length != 3)
            throw new ArgumentException("Fee or currency is invalid.");
        if (request.OtpMaxAttempts <= 0 || request.OtpLockoutMinutes <= 0)
            throw new ArgumentException("OTP policy is invalid.");
    }

    private static void Apply(SystemPolicy policy, SaveSystemPolicyRequest request)
    {
        policy.DefaultApprovalMode = request.DefaultApprovalMode;
        policy.GuestSessionTimeoutMinutes = request.GuestSessionTimeoutMinutes;
        policy.ManualApprovalTimeoutMinutes = request.ManualApprovalTimeoutMinutes;
        policy.CompartmentReservationMinutes = request.CompartmentReservationMinutes;
        policy.OverdueStartAfterHours = request.OverdueStartAfterHours;
        policy.OverdueFeePerHour = request.OverdueFeePerHour;
        policy.Currency = request.Currency.Trim().ToUpperInvariant();
        policy.MaxStorageHours = request.MaxStorageHours;
        policy.ClearanceEligibilityAfterHours = request.ClearanceEligibilityAfterHours;
        policy.ClearanceNoticeBeforeHours = request.ClearanceNoticeBeforeHours;
        policy.PickupReminderStartDay = request.PickupReminderStartDay;
        policy.OtpMaxAttempts = request.OtpMaxAttempts;
        policy.OtpLockoutMinutes = request.OtpLockoutMinutes;
        policy.EnableOtp = request.EnableOtp;
        policy.EnableRemoteUnlock = request.EnableRemoteUnlock;
        policy.EnableFaceRecognition = request.EnableFaceRecognition;
        policy.EffectiveFrom = request.EffectiveFrom;
    }

    private void AddAudit(Guid actor, string action, Guid id, string details, DateTimeOffset now) =>
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actor,
            Action = action,
            EntityType = nameof(SystemPolicy),
            EntityId = id,
            Result = AuditLogResult.Succeeded,
            Details = details,
            OccurredAt = now
        });

    private static SystemPolicyResponse Map(SystemPolicy item) => new(
        item.Id, item.Version, item.DefaultApprovalMode, item.GuestSessionTimeoutMinutes,
        item.ManualApprovalTimeoutMinutes, item.CompartmentReservationMinutes,
        item.OverdueStartAfterHours, item.OverdueFeePerHour, item.Currency, item.MaxStorageHours,
        item.ClearanceEligibilityAfterHours, item.ClearanceNoticeBeforeHours, item.PickupReminderStartDay,
        item.OtpMaxAttempts, item.OtpLockoutMinutes, item.EnableOtp, item.EnableRemoteUnlock,
        item.EnableFaceRecognition, item.EffectiveFrom, item.EffectiveTo, item.IsActive, item.CreatedAt);
}
