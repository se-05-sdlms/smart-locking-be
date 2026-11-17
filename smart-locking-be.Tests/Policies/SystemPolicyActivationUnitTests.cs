using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Policies;

public sealed class SystemPolicyActivationUnitTests
{
    private readonly Guid _adminId = Guid.NewGuid();

    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static SystemPolicyService CreateService(ApplicationDbContext db) =>
        new(db, TimeProvider.System);

    private static SaveSystemPolicyRequest CreateValidRequest(
        decimal feePerHour = 5000m,
        string currency = "VND",
        IncidentSlaPolicy? sla = null)
    {
        return new SaveSystemPolicyRequest(
            DefaultApprovalMode: DeliveryApprovalMode.Auto,
            GuestSessionTimeoutMinutes: 15,
            ManualApprovalTimeoutMinutes: 30,
            CompartmentReservationMinutes: 10,
            OverdueStartAfterHours: 168, // 7 days
            OverdueFeePerHour: feePerHour,
            Currency: currency,
            MaxStorageHours: 336, // 14 days
            ClearanceEligibilityAfterHours: 336,
            ClearanceNoticeBeforeHours: 24,
            PickupReminderStartDay: 5,
            OtpMaxAttempts: 5,
            OtpLockoutMinutes: 15,
            EnableOtp: true,
            EnableRemoteUnlock: true,
            EnableBluetoothUnlock: false,
            EffectiveFrom: DateTimeOffset.UtcNow,
            IncidentSla: sla
        );
    }

    [Fact]
    public async Task ActivateAsync_DraftPolicy_ActivatesTargetAndDeactivatesPreviousActivePolicy_EnsuringSingleActive()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = CreateService(db);

        var policy1Response = await service.CreateAsync(_adminId, CreateValidRequest());
        await service.ActivateAsync(_adminId, policy1Response.Id);

        var policy2Response = await service.CreateAsync(_adminId, CreateValidRequest());

        // Verify pre-condition: policy 1 active, policy 2 inactive
        var initialActiveList = await db.SystemPolicies.Where(p => p.IsActive).ToListAsync();
        Assert.Single(initialActiveList);
        Assert.Equal(policy1Response.Id, initialActiveList[0].Id);

        // Act
        var activated2 = await service.ActivateAsync(_adminId, policy2Response.Id);

        // Assert
        Assert.True(activated2.IsActive);
        Assert.Null(activated2.EffectiveTo);

        // Exactly one policy is active in the database
        var activePolicies = await db.SystemPolicies.Where(p => p.IsActive).ToListAsync();
        Assert.Single(activePolicies);
        Assert.Equal(policy2Response.Id, activePolicies[0].Id);

        // Previous policy is deactivated
        var previousPolicy = await db.SystemPolicies.SingleAsync(p => p.Id == policy1Response.Id);
        Assert.False(previousPolicy.IsActive);
        Assert.NotNull(previousPolicy.EffectiveTo);
        Assert.Equal(activated2.EffectiveFrom, previousPolicy.EffectiveTo);

        // Activation audit log recorded
        var audit = await db.AuditLogs.SingleOrDefaultAsync(a =>
            a.Action == "SystemPolicy.Activated" && a.EntityId == policy2Response.Id);
        Assert.NotNull(audit);
        Assert.Equal(_adminId, audit.ActorUserId);
        Assert.Equal(AuditLogResult.Succeeded, audit.Result);
    }

    [Fact]
    public async Task ActivateAsync_PreservesAllPolicyConfigurationParameters()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = CreateService(db);

        var customSla = new IncidentSlaPolicy(
            CriticalAcknowledgeMinutes: 10,
            CriticalResolveMinutes: 45,
            HighAcknowledgeMinutes: 20,
            HighResolveMinutes: 180,
            MediumAcknowledgeMinutes: 60,
            MediumResolveMinutes: 720,
            LowAcknowledgeMinutes: 240,
            LowResolveMinutes: 2880
        );

        var opEligibleUsers = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var opAreas = new[] { "District 1", "District 2" };

        var detailedRequest = new SaveSystemPolicyRequest(
            DefaultApprovalMode: DeliveryApprovalMode.Manual,
            GuestSessionTimeoutMinutes: 20,
            ManualApprovalTimeoutMinutes: 45,
            CompartmentReservationMinutes: 15,
            OverdueStartAfterHours: 168,
            OverdueFeePerHour: 12500m,
            Currency: "USD",
            MaxStorageHours: 336,
            ClearanceEligibilityAfterHours: 360,
            ClearanceNoticeBeforeHours: 48,
            PickupReminderStartDay: 4,
            OtpMaxAttempts: 3,
            OtpLockoutMinutes: 30,
            EnableOtp: true,
            EnableRemoteUnlock: false,
            EnableBluetoothUnlock: true,
            EffectiveFrom: DateTimeOffset.UtcNow,
            OperatorReplacementRadiusKm: 15.5,
            OperatorReplacementMaxLoad: 8,
            OperatorReplacementAreas: opAreas,
            OperatorReplacementEligibleUserIds: opEligibleUsers,
            IncidentSla: customSla
        );

        var created = await service.CreateAsync(_adminId, detailedRequest);

        // Act
        var activated = await service.ActivateAsync(_adminId, created.Id);

        // Assert - verify returned DTO
        Assert.Equal(created.Id, activated.Id);
        Assert.True(activated.IsActive);
        Assert.Equal(DeliveryApprovalMode.Manual, activated.DefaultApprovalMode);
        Assert.Equal(20, activated.GuestSessionTimeoutMinutes);
        Assert.Equal(45, activated.ManualApprovalTimeoutMinutes);
        Assert.Equal(15, activated.CompartmentReservationMinutes);
        Assert.Equal(168, activated.OverdueStartAfterHours);
        Assert.Equal(12500m, activated.OverdueFeePerHour);
        Assert.Equal("USD", activated.Currency);
        Assert.Equal(336, activated.MaxStorageHours);
        Assert.Equal(360, activated.ClearanceEligibilityAfterHours);
        Assert.Equal(48, activated.ClearanceNoticeBeforeHours);
        Assert.Equal(4, activated.PickupReminderStartDay);
        Assert.Equal(3, activated.OtpMaxAttempts);
        Assert.Equal(30, activated.OtpLockoutMinutes);
        Assert.True(activated.EnableOtp);
        Assert.False(activated.EnableRemoteUnlock);
        Assert.True(activated.EnableBluetoothUnlock);
        Assert.Equal(15.5, activated.OperatorReplacementRadiusKm);
        Assert.Equal(8, activated.OperatorReplacementMaxLoad);

        // Verify SLA targets preserved
        Assert.NotNull(activated.IncidentSla);
        Assert.Equal(10, activated.IncidentSla.CriticalAcknowledgeMinutes);
        Assert.Equal(45, activated.IncidentSla.CriticalResolveMinutes);
        Assert.Equal(20, activated.IncidentSla.HighAcknowledgeMinutes);
        Assert.Equal(180, activated.IncidentSla.HighResolveMinutes);
        Assert.Equal(60, activated.IncidentSla.MediumAcknowledgeMinutes);
        Assert.Equal(720, activated.IncidentSla.MediumResolveMinutes);
        Assert.Equal(240, activated.IncidentSla.LowAcknowledgeMinutes);
        Assert.Equal(2880, activated.IncidentSla.LowResolveMinutes);

        // Assert - verify database entity state
        var dbEntity = await db.SystemPolicies.SingleAsync(p => p.Id == created.Id);
        Assert.True(dbEntity.IsActive);
        Assert.Equal(12500m, dbEntity.OverdueFeePerHour);
        Assert.Equal("USD", dbEntity.Currency);
        Assert.Equal(168, dbEntity.OverdueStartAfterHours);
        Assert.Equal(336, dbEntity.MaxStorageHours);
        Assert.Equal(10, dbEntity.IncidentCriticalAcknowledgeMinutes);
        Assert.Equal(45, dbEntity.IncidentCriticalResolveMinutes);
    }

    [Fact]
    public async Task ActivateAsync_NonExistentPolicy_ThrowsNotFoundException()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ActivateAsync(_adminId, nonExistentId));

        Assert.Equal(PolicyErrorCodes.NotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_ActivePolicy_ThrowsConflictException()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = CreateService(db);

        var policy = await service.CreateAsync(_adminId, CreateValidRequest());
        await service.ActivateAsync(_adminId, policy.Id);

        var updateRequest = CreateValidRequest(feePerHour: 9999m);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(_adminId, policy.Id, updateRequest));

        Assert.Equal(PolicyErrorCodes.Immutable, ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_DraftPolicy_Succeeds()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = CreateService(db);

        var policy = await service.CreateAsync(_adminId, CreateValidRequest(feePerHour: 5000m));
        var updateRequest = CreateValidRequest(feePerHour: 8000m);

        // Act
        var updated = await service.UpdateAsync(_adminId, policy.Id, updateRequest);

        // Assert
        Assert.Equal(8000m, updated.OverdueFeePerHour);
        var dbEntity = await db.SystemPolicies.SingleAsync(p => p.Id == policy.Id);
        Assert.Equal(8000m, dbEntity.OverdueFeePerHour);
        Assert.False(dbEntity.IsActive);
    }

    [Fact]
    public async Task ActivateAsync_ReactivatingAlreadyActivePolicy_RemainsActiveWithoutError()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = CreateService(db);

        var policy = await service.CreateAsync(_adminId, CreateValidRequest());
        await service.ActivateAsync(_adminId, policy.Id);

        // Act
        var reactivated = await service.ActivateAsync(_adminId, policy.Id);

        // Assert
        Assert.True(reactivated.IsActive);
        var activePolicies = await db.SystemPolicies.Where(p => p.IsActive).ToListAsync();
        Assert.Single(activePolicies);
        Assert.Equal(policy.Id, activePolicies[0].Id);
    }
}
