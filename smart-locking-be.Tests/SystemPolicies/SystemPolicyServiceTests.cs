using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.SystemPolicies;

public sealed class SystemPolicyServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static (User Admin, SystemPolicy Policy) SeedAdminAndPolicy(ApplicationDbContext dbContext)
    {
        var admin = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@boxora.com",
            Role = UserRole.Administrator,
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(admin);

        var policy = new SystemPolicy
        {
            Id = Guid.NewGuid(),
            Version = 1,
            DefaultApprovalMode = DeliveryApprovalMode.Manual,
            GuestSessionTimeoutMinutes = 15,
            ManualApprovalTimeoutMinutes = 10,
            CompartmentReservationMinutes = 15,
            OverdueStartAfterHours = 24,
            OverdueFeePerHour = 5000m,
            Currency = "VND",
            MaxStorageHours = 72,
            ClearanceEligibilityAfterHours = 48,
            ClearanceNoticeBeforeHours = 12,
            OtpMaxAttempts = 3,
            OtpLockoutMinutes = 30,
            EnableOtp = true,
            EnableRemoteUnlock = true,
            EnableFaceRecognition = false,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo = null,
            IsActive = true,
            CreatedByUserId = admin.Id,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            SystemPolicyId = policy.Id,
            EventType = "ParcelOverdue",
            Channel = NotificationChannel.Push,
            LeadTimeMinutes = 60,
            IsEnabled = true
        };
        policy.NotificationRules.Add(rule);

        dbContext.SystemPolicies.Add(policy);
        dbContext.SaveChanges();

        return (admin, policy);
    }

    [Fact]
    public async Task GetActivePolicyAsync_ReturnsCurrentActivePolicyAndRules()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (_, policy) = SeedAdminAndPolicy(dbContext);

        // Add an inactive old policy
        dbContext.SystemPolicies.Add(new SystemPolicy
        {
            Id = Guid.NewGuid(),
            Version = 0,
            Currency = "VND",
            IsActive = false,
            CreatedByUserId = policy.CreatedByUserId
        });
        await dbContext.SaveChangesAsync();

        var service = new SystemPolicyService(dbContext);
        var result = await service.GetActivePolicyAsync();

        Assert.NotNull(result);
        Assert.Equal(policy.Id, result.Id);
        Assert.Equal(1, result.Version);
        Assert.True(result.IsActive);
        Assert.Equal("VND", result.Currency);
        Assert.Single(result.NotificationRules);
        Assert.Equal("ParcelOverdue", result.NotificationRules[0].EventType);
    }

    [Fact]
    public async Task GetActivePolicyAsync_WhenNoActivePolicy_ThrowsKeyNotFoundException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new SystemPolicyService(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetActivePolicyAsync());
    }

    [Fact]
    public async Task UpdatePolicyAsync_WhenValid_CreatesNewVersion_DeactivatesPreviousPolicy()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (admin, oldPolicy) = SeedAdminAndPolicy(dbContext);
        var service = new SystemPolicyService(dbContext);

        var request = new UpdateSystemPolicyRequest(
            DefaultApprovalMode: DeliveryApprovalMode.Auto,
            GuestSessionTimeoutMinutes: 20,
            ManualApprovalTimeoutMinutes: 15,
            CompartmentReservationMinutes: 30,
            OverdueStartAfterHours: 12,
            OverdueFeePerHour: 10000m,
            Currency: "VND",
            MaxStorageHours: 96,
            ClearanceEligibilityAfterHours: 72,
            ClearanceNoticeBeforeHours: 24,
            OtpMaxAttempts: 5,
            OtpLockoutMinutes: 60,
            EnableOtp: true,
            EnableRemoteUnlock: true,
            EnableFaceRecognition: true,
            ExpectedVersion: 1,
            NotificationRules: new List<UpdateNotificationRuleRequest>
            {
                new("ParcelStored", NotificationChannel.Push, null, true),
                new("ParcelOverdue", NotificationChannel.Sms, 30, true)
            }
        );

        var response = await service.UpdatePolicyAsync(admin.Id, request, "127.0.0.1");

        Assert.NotNull(response);
        Assert.Equal(2, response.Version);
        Assert.True(response.IsActive);
        Assert.Equal(DeliveryApprovalMode.Auto, response.DefaultApprovalMode);
        Assert.Equal(10000m, response.OverdueFeePerHour);
        Assert.Equal(2, response.NotificationRules.Count);

        // Verify database state
        var reloadedOld = await dbContext.SystemPolicies.FindAsync(oldPolicy.Id);
        Assert.NotNull(reloadedOld);
        Assert.False(reloadedOld.IsActive);
        Assert.NotNull(reloadedOld.EffectiveTo);

        var activePolicies = await dbContext.SystemPolicies.Where(p => p.IsActive).ToListAsync();
        Assert.Single(activePolicies);
        Assert.Equal(2, activePolicies[0].Version);
    }

    [Fact]
    public async Task UpdatePolicyAsync_PreservesHistoricalDeliveryRequestSnapshot()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (admin, oldPolicy) = SeedAdminAndPolicy(dbContext);

        var deliveryRequest = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            LockerId = Guid.NewGuid(),
            SystemPolicyId = oldPolicy.Id,
            GuestSessionTokenHash = "hash123",
            Status = DeliveryRequestStatus.Deposited,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-10)
        };
        dbContext.DeliveryRequests.Add(deliveryRequest);
        await dbContext.SaveChangesAsync();

        var service = new SystemPolicyService(dbContext);
        var request = new UpdateSystemPolicyRequest(
            DefaultApprovalMode: DeliveryApprovalMode.Manual,
            GuestSessionTimeoutMinutes: 30,
            ManualApprovalTimeoutMinutes: 20,
            CompartmentReservationMinutes: 20,
            OverdueStartAfterHours: 48,
            OverdueFeePerHour: 20000m,
            Currency: "VND",
            MaxStorageHours: 120,
            ClearanceEligibilityAfterHours: 96,
            ClearanceNoticeBeforeHours: 12,
            OtpMaxAttempts: 3,
            OtpLockoutMinutes: 15,
            EnableOtp: true,
            EnableRemoteUnlock: false,
            EnableFaceRecognition: false
        );

        await service.UpdatePolicyAsync(admin.Id, request, "127.0.0.1");

        // Verify historical delivery request is untouched and still points to old policy v1
        var reloadedDelivery = await dbContext.DeliveryRequests
            .Include(d => d.SystemPolicy)
            .FirstOrDefaultAsync(d => d.Id == deliveryRequest.Id);

        Assert.NotNull(reloadedDelivery);
        Assert.Equal(oldPolicy.Id, reloadedDelivery.SystemPolicyId);
        Assert.Equal(1, reloadedDelivery.SystemPolicy.Version);
        Assert.Equal(5000m, reloadedDelivery.SystemPolicy.OverdueFeePerHour);
        Assert.False(reloadedDelivery.SystemPolicy.IsActive);
    }

    [Fact]
    public async Task UpdatePolicyAsync_WhenUserNotAdministrator_ThrowsUnauthorizedAccessException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        SeedAdminAndPolicy(dbContext);

        var resident = new User
        {
            Id = Guid.NewGuid(),
            Email = "resident@boxora.com",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Users.Add(resident);
        await dbContext.SaveChangesAsync();

        var service = new SystemPolicyService(dbContext);
        var request = CreateValidRequest();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdatePolicyAsync(resident.Id, request, "127.0.0.1"));
    }

    [Fact]
    public async Task UpdatePolicyAsync_WhenFieldRangesAreInvalid_ThrowsArgumentException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (admin, _) = SeedAdminAndPolicy(dbContext);
        var service = new SystemPolicyService(dbContext);

        // GuestSessionTimeoutMinutes <= 0
        var req1 = CreateValidRequest() with { GuestSessionTimeoutMinutes = 0 };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req1, null));

        // ManualApprovalTimeoutMinutes <= 0
        var req2 = CreateValidRequest() with { ManualApprovalTimeoutMinutes = -5 };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req2, null));

        // CompartmentReservationMinutes <= 0
        var req3 = CreateValidRequest() with { CompartmentReservationMinutes = 0 };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req3, null));

        // OverdueStartAfterHours < 0
        var req4 = CreateValidRequest() with { OverdueStartAfterHours = -1 };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req4, null));

        // OverdueFeePerHour < 0
        var req5 = CreateValidRequest() with { OverdueFeePerHour = -100m };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req5, null));

        // Currency invalid length
        var req6 = CreateValidRequest() with { Currency = "VIETNAM" };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req6, null));

        // MaxStorageHours <= 0
        var req7 = CreateValidRequest() with { MaxStorageHours = 0 };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req7, null));

        // OtpMaxAttempts <= 0
        var req8 = CreateValidRequest() with { OtpMaxAttempts = 0 };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req8, null));

        // OtpLockoutMinutes <= 0
        var req9 = CreateValidRequest() with { OtpLockoutMinutes = 0 };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req9, null));
    }

    [Fact]
    public async Task UpdatePolicyAsync_WhenCrossFieldValidationsFail_ThrowsArgumentException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (admin, _) = SeedAdminAndPolicy(dbContext);
        var service = new SystemPolicyService(dbContext);

        // MaxStorageHours <= OverdueStartAfterHours
        var req1 = CreateValidRequest() with { OverdueStartAfterHours = 48, MaxStorageHours = 24 };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req1, null));

        // ClearanceEligibilityAfterHours < OverdueStartAfterHours
        var req2 = CreateValidRequest() with { OverdueStartAfterHours = 48, ClearanceEligibilityAfterHours = 24 };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req2, null));

        // ClearanceEligibilityAfterHours > MaxStorageHours
        var req3 = CreateValidRequest() with { ClearanceEligibilityAfterHours = 100, MaxStorageHours = 72 };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req3, null));

        // ClearanceNoticeBeforeHours >= ClearanceEligibilityAfterHours
        var req4 = CreateValidRequest() with { ClearanceEligibilityAfterHours = 48, ClearanceNoticeBeforeHours = 50 };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req4, null));

        // All unlock methods disabled
        var req5 = CreateValidRequest() with { EnableOtp = false, EnableRemoteUnlock = false, EnableFaceRecognition = false };
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, req5, null));
    }

    [Fact]
    public async Task UpdatePolicyAsync_WhenDuplicateNotificationRule_ThrowsArgumentException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (admin, _) = SeedAdminAndPolicy(dbContext);
        var service = new SystemPolicyService(dbContext);

        var request = CreateValidRequest() with
        {
            NotificationRules = new List<UpdateNotificationRuleRequest>
            {
                new("ParcelOverdue", NotificationChannel.Push, 30, true),
                new("ParcelOverdue", NotificationChannel.Push, 60, false)
            }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePolicyAsync(admin.Id, request, null));
    }

    [Fact]
    public async Task UpdatePolicyAsync_WhenVersionMismatch_ThrowsInvalidOperationException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (admin, _) = SeedAdminAndPolicy(dbContext); // Version 1
        var service = new SystemPolicyService(dbContext);

        var request = CreateValidRequest() with { ExpectedVersion = 99 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdatePolicyAsync(admin.Id, request, null));
    }

    [Fact]
    public async Task UpdatePolicyAsync_WritesAuditLog_WithoutSecrets()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (admin, _) = SeedAdminAndPolicy(dbContext);
        var service = new SystemPolicyService(dbContext);

        var request = CreateValidRequest();
        var response = await service.UpdatePolicyAsync(admin.Id, request, "192.168.1.100");

        var auditLog = await dbContext.AuditLogs.FirstOrDefaultAsync(a => a.EntityId == response.Id);
        Assert.NotNull(auditLog);
        Assert.Equal("UpdateSystemPolicy", auditLog.Action);
        Assert.Equal("SystemPolicy", auditLog.EntityType);
        Assert.Equal(admin.Id, auditLog.ActorUserId);
        Assert.Equal(AuditLogResult.Succeeded, auditLog.Result);
        Assert.Equal("192.168.1.100", auditLog.IpAddress);
        Assert.NotNull(auditLog.Details);
        Assert.Contains("v1", auditLog.Details);
        Assert.Contains("v2", auditLog.Details);
        Assert.DoesNotContain("password", auditLog.Details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", auditLog.Details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", auditLog.Details, StringComparison.OrdinalIgnoreCase);
    }

    private static UpdateSystemPolicyRequest CreateValidRequest() => new(
        DefaultApprovalMode: DeliveryApprovalMode.Manual,
        GuestSessionTimeoutMinutes: 15,
        ManualApprovalTimeoutMinutes: 10,
        CompartmentReservationMinutes: 15,
        OverdueStartAfterHours: 24,
        OverdueFeePerHour: 5000m,
        Currency: "VND",
        MaxStorageHours: 72,
        ClearanceEligibilityAfterHours: 48,
        ClearanceNoticeBeforeHours: 12,
        OtpMaxAttempts: 3,
        OtpLockoutMinutes: 30,
        EnableOtp: true,
        EnableRemoteUnlock: true,
        EnableFaceRecognition: false
    );
}
