using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Residents;

public sealed class ResidentServiceUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "RESIDENT")]
    public async Task GetProfileAsync_CreatesLegacyProfileWhenMissing()
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
    public async Task UpdateProfileAsync_TrimsAndPersistsValues()
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
    public async Task UpdateApprovalModeAsync_PersistsMode()
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
    public async Task GetProfileAsync_RejectsNonResidentAccess()
    {
        await using var db = CreateDbContext();
        var user = ResidentUser();
        user.Role = UserRole.Administrator;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => new ResidentService(db).GetProfileAsync(user.Id));
    }

    private static ApplicationDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

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
}
