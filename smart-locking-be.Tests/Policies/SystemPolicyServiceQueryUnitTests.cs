using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Policies;

public sealed class SystemPolicyServiceQueryUnitTests
{
    [Fact]
    [Trait("Category", "POLICY-QUERY")]
    public async Task Policies_ListFiltersActiveAndOrdersByVersionDescending()
    {
        await using var db = CreateDbContext();
        db.SystemPolicies.AddRange(Policy(1, false), Policy(3, true), Policy(2, true));
        await db.SaveChangesAsync();

        var result = await new SystemPolicyService(db, TimeProvider.System).GetAsync(true);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { 3, 2 }, result.Items.Select(item => item.Version));
    }

    [Fact]
    [Trait("Category", "POLICY-QUERY")]
    public async Task Policies_GetByIdReturnsPolicy()
    {
        await using var db = CreateDbContext();
        var policy = Policy(4, false);
        db.SystemPolicies.Add(policy);
        await db.SaveChangesAsync();

        var result = await new SystemPolicyService(db, TimeProvider.System).GetByIdAsync(policy.Id);

        Assert.Equal(policy.Id, result.Id);
        Assert.Equal(4, result.Version);
    }

    [Fact]
    [Trait("Category", "POLICY-QUERY")]
    public async Task Policies_GetByIdReturnsNotFoundForUnknownId()
    {
        await using var db = CreateDbContext();

        await Assert.ThrowsAsync<NotFoundException>(() => new SystemPolicyService(db, TimeProvider.System)
            .GetByIdAsync(Guid.NewGuid()));
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static SystemPolicy Policy(int version, bool active) => new()
    {
        Id = Guid.NewGuid(),
        Version = version,
        IsActive = active,
        DefaultApprovalMode = DeliveryApprovalMode.Auto,
        GuestSessionTimeoutMinutes = 30,
        ManualApprovalTimeoutMinutes = 60,
        CompartmentReservationMinutes = 30,
        OverdueStartAfterHours = 168,
        OverdueFeePerHour = 100,
        Currency = "VND",
        MaxStorageHours = 336,
        ClearanceEligibilityAfterHours = 336,
        ClearanceNoticeBeforeHours = 24,
        PickupReminderStartDay = 5,
        OtpMaxAttempts = 5,
        OtpLockoutMinutes = 15,
        EnableOtp = true,
        EffectiveFrom = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
