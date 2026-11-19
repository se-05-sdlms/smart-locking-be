using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Application.DTOs.Subscriptions;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Services;

public sealed class BasicServiceUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "SUBSCRIPTION-PLAN")]
    public async Task SubscriptionPlans_ListFiltersInactiveAndSortsByCode()
    {
        await using var db = CreateDbContext();
        db.SubscriptionPlans.AddRange(
            Plan("BASIC", true),
            Plan("ACTIVE", true),
            Plan("OLD", false));
        await db.SaveChangesAsync();

        var service = new SubscriptionPlanService(db, new FixedTimeProvider(Now));

        var active = await service.GetPlansAsync();
        var all = await service.GetPlansAsync(false);

        Assert.Equal(new[] { "ACTIVE", "BASIC" }, active.Select(item => item.Code));
        Assert.Equal(3, all.Count);
    }

    [Fact]
    [Trait("Category", "SUBSCRIPTION-PLAN")]
    public async Task SubscriptionPlans_CreateTrimsCodeAndPersistsPlan()
    {
        await using var db = CreateDbContext();
        var service = new SubscriptionPlanService(db, new FixedTimeProvider(Now));

        SubscriptionPlanResponse response = await service.CreateAsync(
            new CreateSubscriptionPlanRequest(" basic ", "Basic", "Description", 100, 30, 5), default);

        Assert.Equal("basic", response.Code);
        Assert.Equal(Now, db.SubscriptionPlans.Single().CreatedAt);
    }

    [Theory]
    [InlineData("", "Name", 100, 30, 5)]
    [InlineData("CODE", "", 100, 30, 5)]
    [InlineData("CODE", "Name", -1, 30, 5)]
    [InlineData("CODE", "Name", 100, 0, 5)]
    [InlineData("CODE", "Name", 100, 30, 0)]
    [Trait("Category", "SUBSCRIPTION-PLAN")]
    public async Task SubscriptionPlans_RejectInvalidData(string code, string name, decimal price, int days, int capacity)
    {
        await using var db = CreateDbContext();
        var service = new SubscriptionPlanService(db, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(
            new CreateSubscriptionPlanRequest(code, name, null, price, days, capacity), default));
    }

    [Fact]
    [Trait("Category", "SUBSCRIPTION-PLAN")]
    public async Task SubscriptionPlans_UpdateMissingPlanReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var service = new SubscriptionPlanService(db, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(
            Guid.NewGuid(), new UpdateSubscriptionPlanRequest("Basic", null, 100, 30, 5, true), default));
    }

    [Fact]
    [Trait("Category", "NOTIFICATION")]
    public async Task Notifications_ListUnreadOnlyAndMarkAllRead()
    {
        await using var db = CreateDbContext();
        Guid userId = Guid.NewGuid();
        db.Notifications.AddRange(
            Notification(userId, "new", false, Now.AddMinutes(1)),
            Notification(userId, "old", true, Now),
            Notification(Guid.NewGuid(), "other", false, Now.AddMinutes(2)));
        await db.SaveChangesAsync();
        var service = new NotificationService(db, new FixedTimeProvider(Now.AddHours(1)));

        var unread = await service.GetForUserAsync(userId, true, 10, default);
        var result = await service.MarkAllReadAsync(userId, default);

        Assert.Single(unread);
        Assert.Equal("new", unread.Single().Type);
        Assert.Equal(1, result.UpdatedCount);
        Assert.All(db.Notifications.Where(item => item.UserId == userId), item => Assert.True(item.IsRead));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [Trait("Category", "NOTIFICATION")]
    public async Task Notifications_RejectInvalidLimit(int limit)
    {
        await using var db = CreateDbContext();

        await Assert.ThrowsAsync<BadRequestException>(() => new NotificationService(db, TimeProvider.System)
            .GetForUserAsync(Guid.NewGuid(), false, limit, default));
    }

    [Fact]
    [Trait("Category", "NOTIFICATION")]
    public async Task Notifications_MarkReadRequiresOwningUser()
    {
        await using var db = CreateDbContext();
        var notification = Notification(Guid.NewGuid(), "new", false, Now);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => new NotificationService(db, TimeProvider.System)
            .MarkReadAsync(Guid.NewGuid(), notification.Id, default));
    }

    [Fact]
    [Trait("Category", "RESIDENT")]
    public async Task Resident_GetProfileCreatesLegacyProfileWhenMissing()
    {
        await using var db = CreateDbContext();
        var user = ResidentUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        ResidentProfileResponse response = await new ResidentService(db).GetProfileAsync(user.Id);

        Assert.Equal(user.Id, response.UserId);
        Assert.Equal(user.PhoneNumber, response.FullName);
        Assert.Single(db.ResidentProfiles);
    }

    [Fact]
    [Trait("Category", "RESIDENT")]
    public async Task Resident_UpdateProfileTrimsAndPersistsValues()
    {
        await using var db = CreateDbContext();
        var user = ResidentUser();
        user.ResidentProfile = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FullName = "Old",
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        ResidentProfileResponse response = await new ResidentService(db).UpdateProfileAsync(
            user.Id, new UpdateResidentProfileRequest("  New Name  ", new DateOnly(1990, 1, 1), "  https://image  "));

        Assert.Equal("New Name", response.FullName);
        Assert.Equal("https://image", response.AvatarUrl);
    }

    [Fact]
    [Trait("Category", "RESIDENT")]
    public async Task Resident_UpdateApprovalModePersistsMode()
    {
        await using var db = CreateDbContext();
        var user = ResidentUser();
        user.ResidentProfile = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FullName = "Resident",
            DeliveryApprovalMode = DeliveryApprovalMode.Auto,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        ResidentProfileResponse response = await new ResidentService(db).UpdateApprovalModeAsync(
            user.Id, new UpdateApprovalModeRequest(DeliveryApprovalMode.Manual));

        Assert.Equal(DeliveryApprovalMode.Manual, response.DeliveryApprovalMode);
    }

    [Fact]
    [Trait("Category", "RESIDENT")]
    public async Task Resident_RejectsNonResidentAccess()
    {
        await using var db = CreateDbContext();
        var user = ResidentUser();
        user.Role = UserRole.Administrator;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => new ResidentService(db).GetProfileAsync(user.Id));
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static SubscriptionPlan Plan(string code, bool active) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        Price = 100,
        DurationDays = 30,
        MaxActiveParcels = 5,
        IsActive = active,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static Notification Notification(Guid userId, string type, bool read, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Type = type,
        Title = type,
        Message = type,
        Channel = NotificationChannel.InApp,
        DeliveryStatus = NotificationDeliveryStatus.Sent,
        IsRead = read,
        ReadAt = read ? createdAt : null,
        CreatedAt = createdAt
    };

    private static User ResidentUser() => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = "0912345678",
        PasswordHash = "hash",
        Role = UserRole.Resident,
        Status = UserStatus.Active,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
