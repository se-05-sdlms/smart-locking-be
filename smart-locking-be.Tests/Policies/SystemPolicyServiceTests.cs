using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Policies;

public sealed class SystemPolicyServiceTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static SaveSystemPolicyRequest CreateValidRequest() => new(
        DefaultApprovalMode: DeliveryApprovalMode.Manual,
        GuestSessionTimeoutMinutes: 15,
        ManualApprovalTimeoutMinutes: 10,
        CompartmentReservationMinutes: 10,
        OverdueStartAfterHours: 7 * 24,
        OverdueFeePerHour: 5000m,
        Currency: "VND",
        MaxStorageHours: 14 * 24,
        ClearanceEligibilityAfterHours: 96,
        ClearanceNoticeBeforeHours: 24,
        PickupReminderStartDay: 1,
        OtpMaxAttempts: 5,
        OtpLockoutMinutes: 15,
        EnableOtp: true,
        EnableRemoteUnlock: true,
        EnableFaceRecognition: false,
        EffectiveFrom: DateTimeOffset.UtcNow);

    #region POL-01: GetAsync
    [Fact]
    [Trait("Category", "POL-01")]
    public async Task GetAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        dbContext.SystemPolicies.Add(new SystemPolicy
        {
            Id = Guid.NewGuid(),
            Version = 1,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new SystemPolicyService(dbContext, TimeProvider.System);
        PagedResult<SystemPolicyResponse> result = await service.GetAsync(null, 1, 10, default);

        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    [Trait("Category", "POL-01")]
    public async Task GetAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new SystemPolicyService(dbContext, TimeProvider.System);

        PagedResult<SystemPolicyResponse> result = await service.GetAsync(true, 1, 10, default);

        Assert.Empty(result.Items);
    }

    [Fact]
    [Trait("Category", "POL-01")]
    public async Task GetAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new SystemPolicyService(dbContext, TimeProvider.System);

        PagedResult<SystemPolicyResponse> result = await service.GetAsync(null, 0, 0, default);

        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
    }
    #endregion

    #region POL-02: GetByIdAsync
    [Fact]
    [Trait("Category", "POL-02")]
    public async Task GetByIdAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = new SystemPolicy { Id = Guid.NewGuid(), Version = 1, IsActive = false, CreatedAt = DateTimeOffset.UtcNow };
        dbContext.SystemPolicies.Add(policy);
        await dbContext.SaveChangesAsync();

        var service = new SystemPolicyService(dbContext, TimeProvider.System);
        SystemPolicyResponse result = await service.GetByIdAsync(policy.Id, default);

        Assert.Equal(policy.Id, result.Id);
    }

    [Fact]
    [Trait("Category", "POL-02")]
    public async Task GetByIdAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new SystemPolicyService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetByIdAsync(Guid.NewGuid(), default));
        Assert.Equal("System policy not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "POL-02")]
    public async Task GetByIdAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new SystemPolicyService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetByIdAsync(Guid.Empty, default));
        Assert.Equal("System policy not found.", ex.Message);
    }
    #endregion

    #region POL-03: CreateAsync
    [Fact]
    [Trait("Category", "POL-03")]
    public async Task CreateAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new SystemPolicyService(dbContext, TimeProvider.System);
        var adminId = Guid.NewGuid();

        SystemPolicyResponse result = await service.CreateAsync(adminId, CreateValidRequest(), default);

        Assert.Equal(1, result.Version);
        Assert.Single(dbContext.SystemPolicies);
    }

    [Fact]
    [Trait("Category", "POL-03")]
    public async Task CreateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new SystemPolicyService(dbContext, TimeProvider.System);

        var invalidRequest = CreateValidRequest() with { Currency = "" };
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Guid.NewGuid(), invalidRequest, default));
    }

    [Fact]
    [Trait("Category", "POL-03")]
    public async Task CreateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new SystemPolicyService(dbContext, TimeProvider.System);

        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(Guid.Empty, null!, default));
    }
    #endregion

    #region POL-04: UpdateAsync
    [Fact]
    [Trait("Category", "POL-04")]
    public async Task UpdateAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = new SystemPolicy { Id = Guid.NewGuid(), Version = 1, IsActive = false, CreatedAt = DateTimeOffset.UtcNow };
        dbContext.SystemPolicies.Add(policy);
        await dbContext.SaveChangesAsync();

        var service = new SystemPolicyService(dbContext, TimeProvider.System);
        SystemPolicyResponse result = await service.UpdateAsync(Guid.NewGuid(), policy.Id, CreateValidRequest(), default);

        Assert.Equal(policy.Id, result.Id);
    }

    [Fact]
    [Trait("Category", "POL-04")]
    public async Task UpdateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new SystemPolicyService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), CreateValidRequest(), default));
        Assert.Equal("System policy not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "POL-04")]
    public async Task UpdateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = new SystemPolicy { Id = Guid.NewGuid(), Version = 1, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        dbContext.SystemPolicies.Add(policy);
        await dbContext.SaveChangesAsync();

        var service = new SystemPolicyService(dbContext, TimeProvider.System);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(Guid.NewGuid(), policy.Id, CreateValidRequest(), default));
        Assert.Equal("Active policy is immutable. Create a new version instead.", ex.Message);
    }

    [Fact]
    [Trait("Category", "POL-04")]
    public async Task UpdateAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new SystemPolicyService(dbContext, TimeProvider.System);

        await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateAsync(Guid.Empty, Guid.Empty, null!, default));
    }
    #endregion

    #region POL-05: ActivateAsync
    [Fact]
    [Trait("Category", "POL-05")]
    public async Task ActivateAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var oldPolicy = new SystemPolicy { Id = Guid.NewGuid(), Version = 1, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var newPolicy = new SystemPolicy { Id = Guid.NewGuid(), Version = 2, IsActive = false, CreatedAt = DateTimeOffset.UtcNow };
        dbContext.SystemPolicies.AddRange(oldPolicy, newPolicy);
        await dbContext.SaveChangesAsync();

        var service = new SystemPolicyService(dbContext, TimeProvider.System);
        SystemPolicyResponse result = await service.ActivateAsync(Guid.NewGuid(), newPolicy.Id, default);

        Assert.True(result.IsActive);
        Assert.False(dbContext.SystemPolicies.Single(p => p.Id == oldPolicy.Id).IsActive);
    }

    [Fact]
    [Trait("Category", "POL-05")]
    public async Task ActivateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new SystemPolicyService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ActivateAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.Equal("System policy not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "POL-05")]
    public async Task ActivateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new SystemPolicyService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ActivateAsync(Guid.Empty, Guid.Empty, default));
        Assert.Equal("System policy not found.", ex.Message);
    }
    #endregion
}
