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
            ?? throw new NotFoundException(PolicyErrorCodes.NotFound, "System policy not found."));

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
            ?? throw new NotFoundException(PolicyErrorCodes.NotFound, "System policy not found.");
        if (policy.IsActive) throw new ConflictException(PolicyErrorCodes.Immutable, "Active policy is immutable. Create a new version instead.");
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
        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({EffectiveOperatorAssignmentQueries.AllocationLock})", cancellationToken);
        SystemPolicy target = await dbContext.SystemPolicies.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(PolicyErrorCodes.NotFound, "System policy not found.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<SystemPolicy> active = await dbContext.SystemPolicies.Where(item => item.IsActive && item.Id != id).ToListAsync(cancellationToken);
        foreach (SystemPolicy policy in active)
        {
            policy.IsActive = false;
            policy.EffectiveTo = now;
        }
        var obsoleteCoverage = await dbContext.TemporaryOperatorAssignments.Include(x => x.Absence)
            .Where(x => x.PolicyId != id && (x.Status == TemporaryAssignmentStatus.Approved || x.Status == TemporaryAssignmentStatus.Active))
            .ToListAsync(cancellationToken);
        var adminIds = await dbContext.Users.Where(x => x.Role == UserRole.Administrator && x.Status == UserStatus.Active)
            .Select(x => x.Id).ToListAsync(cancellationToken);
        foreach (var coverage in obsoleteCoverage)
        {
            coverage.Status = TemporaryAssignmentStatus.NeedsApproval;
            coverage.ActivatedAt = null;
            coverage.StatusReason = "System policy changed. Explicit Admin reapproval is required.";
            AddAudit(adminUserId, "OperatorReplacement.ReapprovalRequired", coverage.Id, coverage.StatusReason, now);
            foreach (Guid recipient in adminIds.Concat(coverage.OperatorUserId is { } substitute ? new[] { substitute } : Array.Empty<Guid>()).Distinct())
                dbContext.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = recipient,
                    Type = "OperatorReplacement",
                    Channel = NotificationChannel.InApp,
                    Title = "Operator coverage requires reapproval",
                    Message = $"Locker {coverage.LockerId}; {coverage.StatusReason}",
                    DeliveryStatus = NotificationDeliveryStatus.Sent,
                    CreatedAt = now,
                    SentAt = now
                });
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
        bool anyCoverage = request.OperatorReplacementRadiusKm.HasValue || request.OperatorReplacementMaxLoad.HasValue ||
            request.OperatorReplacementAreas is not null || request.OperatorReplacementEligibleUserIds is not null;
        if (anyCoverage && (request.OperatorReplacementRadiusKm is not > 0 ||
            !double.IsFinite(request.OperatorReplacementRadiusKm.Value) || request.OperatorReplacementMaxLoad is not > 0 ||
            request.OperatorReplacementAreas is not { Length: > 0 } || request.OperatorReplacementAreas.Any(string.IsNullOrWhiteSpace) ||
            request.OperatorReplacementAreas.Any(x => x.Trim().Length > 100) ||
            request.OperatorReplacementEligibleUserIds is not { Length: > 0 } || request.OperatorReplacementEligibleUserIds.Contains(Guid.Empty)))
            throw BadRequestException.For("operatorReplacement", "Provide a positive radius and load, service areas and eligible operator IDs together.");
        if (request.GuestSessionTimeoutMinutes <= 0 || request.ManualApprovalTimeoutMinutes <= 0 || request.CompartmentReservationMinutes <= 0)
            throw BadRequestException.For("timeout", "Timeout values must be positive.");
        if (request.OverdueStartAfterHours != 7 * 24 || request.MaxStorageHours != 14 * 24)
            throw BadRequestException.For("storageTimeline", "Storage timeline must be 7 normal days followed by 7 overdue days.");
        if (request.PickupReminderStartDay <= 0 || request.PickupReminderStartDay > Math.Ceiling(request.MaxStorageHours / 24d))
            throw BadRequestException.For("pickupReminderStartDay", "Pickup reminder start day is outside the storage period.");
        if (request.OverdueFeePerHour < 0 || request.Currency.Trim().Length != 3)
            throw BadRequestException.For("fee", "Fee or currency is invalid.");
        if (request.OtpMaxAttempts <= 0 || request.OtpLockoutMinutes <= 0)
            throw BadRequestException.For("otpPolicy", "OTP policy is invalid.");
        IncidentSlaPolicy sla = request.IncidentSla ?? new();
        (int Acknowledge, int Resolve)[] limits = [(sla.CriticalAcknowledgeMinutes, sla.CriticalResolveMinutes),
            (sla.HighAcknowledgeMinutes, sla.HighResolveMinutes), (sla.MediumAcknowledgeMinutes, sla.MediumResolveMinutes),
            (sla.LowAcknowledgeMinutes, sla.LowResolveMinutes)];
        if (limits.Any(limit => limit.Acknowledge <= 0 || limit.Resolve < limit.Acknowledge))
            throw BadRequestException.For("incidentSla", "SLA targets must be positive; resolution must not precede acknowledgement.");
    }

    private static void Apply(SystemPolicy policy, SaveSystemPolicyRequest request)
    {
        policy.OperatorReplacementRadiusKm = request.OperatorReplacementRadiusKm;
        policy.OperatorReplacementMaxLoad = request.OperatorReplacementMaxLoad;
        policy.OperatorReplacementAreas = request.OperatorReplacementAreas?.Select(x => x.Trim()).Distinct().Order().ToArray();
        policy.OperatorReplacementEligibleUserIds = request.OperatorReplacementEligibleUserIds?.Distinct().Order().ToArray();
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
        policy.EnableBluetoothUnlock = request.EnableBluetoothUnlock;
        policy.EffectiveFrom = request.EffectiveFrom;
        IncidentSlaPolicy sla = request.IncidentSla ?? new();
        policy.IncidentCriticalAcknowledgeMinutes = sla.CriticalAcknowledgeMinutes;
        policy.IncidentCriticalResolveMinutes = sla.CriticalResolveMinutes;
        policy.IncidentHighAcknowledgeMinutes = sla.HighAcknowledgeMinutes;
        policy.IncidentHighResolveMinutes = sla.HighResolveMinutes;
        policy.IncidentMediumAcknowledgeMinutes = sla.MediumAcknowledgeMinutes;
        policy.IncidentMediumResolveMinutes = sla.MediumResolveMinutes;
        policy.IncidentLowAcknowledgeMinutes = sla.LowAcknowledgeMinutes;
        policy.IncidentLowResolveMinutes = sla.LowResolveMinutes;
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
        item.EnableBluetoothUnlock, item.EffectiveFrom, item.EffectiveTo, item.IsActive, item.CreatedAt,
        item.OperatorReplacementRadiusKm, item.OperatorReplacementMaxLoad, item.OperatorReplacementAreas, item.OperatorReplacementEligibleUserIds,
        new IncidentSlaPolicy(item.IncidentCriticalAcknowledgeMinutes, item.IncidentCriticalResolveMinutes,
            item.IncidentHighAcknowledgeMinutes, item.IncidentHighResolveMinutes,
            item.IncidentMediumAcknowledgeMinutes, item.IncidentMediumResolveMinutes,
            item.IncidentLowAcknowledgeMinutes, item.IncidentLowResolveMinutes));
}
