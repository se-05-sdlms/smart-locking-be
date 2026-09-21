using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Residents;

public sealed class ResidentServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetProfileAsync_WhenUserNotFound_ThrowsKeyNotFoundException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var tokenHashService = new Sha256TokenHashService();
        var service = new ResidentService(dbContext, tokenHashService);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetProfileAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetProfileAsync_WhenUserIsLocked_ThrowsInvalidOperationException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0987654321",
            Email = "locked@boxora.com",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Locked
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var tokenHashService = new Sha256TokenHashService();
        var service = new ResidentService(dbContext, tokenHashService);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetProfileAsync(user.Id));
        Assert.Contains("khóa", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetProfileAsync_WhenUserIsDisabled_ThrowsInvalidOperationException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0987654322",
            Email = "disabled@boxora.com",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Disabled
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var tokenHashService = new Sha256TokenHashService();
        var service = new ResidentService(dbContext, tokenHashService);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetProfileAsync(user.Id));
        Assert.Contains("vô hiệu hóa", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetProfileAsync_WhenProfileIsNull_InitializesDefaultProfileAndReturnsResponse()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            Email = "newresident@boxora.com",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var tokenHashService = new Sha256TokenHashService();
        var service = new ResidentService(dbContext, tokenHashService);

        var response = await service.GetProfileAsync(user.Id);

        Assert.NotNull(response);
        Assert.Equal(user.Id, response.UserId);
        Assert.Equal("0912345678", response.PhoneNumber);
        Assert.Equal("newresident@boxora.com", response.Email);
        Assert.Equal("0912345678", response.FullName);
        Assert.Equal(DeliveryApprovalMode.Auto, response.DeliveryApprovalMode);
        Assert.NotEqual(default, response.PersonalQrIssuedAt);

        var persistedProfile = await dbContext.ResidentProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
        Assert.NotNull(persistedProfile);
        Assert.NotEmpty(persistedProfile.PersonalQrTokenHash);
    }

    [Fact]
    public async Task GetProfileAsync_WhenProfileExists_ReturnsExpectedProfile()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var issuedAt = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = userId,
            PhoneNumber = "0900000001",
            Email = "resident@boxora.com",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var profile = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FullName = "Nguyen Van A",
            DateOfBirth = new DateOnly(1995, 5, 20),
            AvatarUrl = "https://boxora.com/avatar.png",
            DeliveryApprovalMode = DeliveryApprovalMode.Auto,
            PersonalQrTokenHash = "testhash",
            PersonalQrIssuedAt = issuedAt,
            FaceRecognitionEnabled = false
        };
        dbContext.Users.Add(user);
        dbContext.ResidentProfiles.Add(profile);
        await dbContext.SaveChangesAsync();

        var tokenHashService = new Sha256TokenHashService();
        var service = new ResidentService(dbContext, tokenHashService);

        var response = await service.GetProfileAsync(userId);

        Assert.NotNull(response);
        Assert.Equal(userId, response.UserId);
        Assert.Equal("Nguyen Van A", response.FullName);
        Assert.Equal(new DateOnly(1995, 5, 20), response.DateOfBirth);
        Assert.Equal("https://boxora.com/avatar.png", response.AvatarUrl);
        Assert.Equal(DeliveryApprovalMode.Auto, response.DeliveryApprovalMode);
        Assert.Equal(issuedAt, response.PersonalQrIssuedAt);
    }

    [Fact]
    public async Task UpdateProfileAsync_WithWhitespaceFullName_ThrowsArgumentException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0900000002",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var tokenHashService = new Sha256TokenHashService();
        var service = new ResidentService(dbContext, tokenHashService);

        var request = new UpdateResidentProfileRequest("   ", null, null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateProfileAsync(user.Id, request));
    }

    [Fact]
    public async Task UpdateProfileAsync_WithFutureDateOfBirth_ThrowsArgumentException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0900000003",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var tokenHashService = new Sha256TokenHashService();
        var service = new ResidentService(dbContext, tokenHashService);

        var request = new UpdateResidentProfileRequest(
            "Valid Name",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateProfileAsync(user.Id, request));
    }

    [Fact]
    public async Task UpdateProfileAsync_WithValidData_UpdatesProfileFields()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            PhoneNumber = "0900000004",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var profile = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FullName = "Old Name",
            DeliveryApprovalMode = DeliveryApprovalMode.Manual,
            PersonalQrTokenHash = "hash123",
            PersonalQrIssuedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(user);
        dbContext.ResidentProfiles.Add(profile);
        await dbContext.SaveChangesAsync();

        var tokenHashService = new Sha256TokenHashService();
        var service = new ResidentService(dbContext, tokenHashService);

        var request = new UpdateResidentProfileRequest(
            "Tran Thi B",
            new DateOnly(2000, 1, 15),
            "https://boxora.com/b.jpg");

        var response = await service.UpdateProfileAsync(userId, request);

        Assert.Equal("Tran Thi B", response.FullName);
        Assert.Equal(new DateOnly(2000, 1, 15), response.DateOfBirth);
        Assert.Equal("https://boxora.com/b.jpg", response.AvatarUrl);

        var updatedProfile = await dbContext.ResidentProfiles.FirstAsync(p => p.UserId == userId);
        Assert.Equal("Tran Thi B", updatedProfile.FullName);
        Assert.Equal(new DateOnly(2000, 1, 15), updatedProfile.DateOfBirth);
        Assert.Equal("https://boxora.com/b.jpg", updatedProfile.AvatarUrl);
    }

    [Fact]
    public async Task UpdateApprovalModeAsync_SwitchesModeSuccessfully()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            PhoneNumber = "0900000005",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var profile = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FullName = "Test Resident",
            DeliveryApprovalMode = DeliveryApprovalMode.Manual,
            PersonalQrTokenHash = "hash123",
            PersonalQrIssuedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(user);
        dbContext.ResidentProfiles.Add(profile);
        await dbContext.SaveChangesAsync();

        var tokenHashService = new Sha256TokenHashService();
        var service = new ResidentService(dbContext, tokenHashService);

        var response = await service.UpdateApprovalModeAsync(
            userId,
            new UpdateApprovalModeRequest(DeliveryApprovalMode.Auto));

        Assert.Equal(DeliveryApprovalMode.Auto, response.DeliveryApprovalMode);

        var updatedProfile = await dbContext.ResidentProfiles.FirstAsync(p => p.UserId == userId);
        Assert.Equal(DeliveryApprovalMode.Auto, updatedProfile.DeliveryApprovalMode);
    }

    [Fact]
    public async Task GetPersonalQrAsync_RotatesPersonalQrToken_ReturnsTokenAndPersistsHash()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            PhoneNumber = "0900000006",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var profile = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FullName = "Test Resident",
            DeliveryApprovalMode = DeliveryApprovalMode.Manual,
            PersonalQrTokenHash = "initial-hash",
            PersonalQrIssuedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        dbContext.Users.Add(user);
        dbContext.ResidentProfiles.Add(profile);
        await dbContext.SaveChangesAsync();

        var tokenHashService = new Sha256TokenHashService();
        var service = new ResidentService(dbContext, tokenHashService);

        var response = await service.GetPersonalQrAsync(userId);

        Assert.NotNull(response);
        Assert.NotEmpty(response.QrToken);
        Assert.True(response.IssuedAt > DateTimeOffset.UtcNow.AddMinutes(-1));

        var updatedProfile = await dbContext.ResidentProfiles.FirstAsync(p => p.UserId == userId);
        Assert.NotEqual("initial-hash", updatedProfile.PersonalQrTokenHash);
        Assert.Equal(tokenHashService.HashToken(response.QrToken), updatedProfile.PersonalQrTokenHash);
    }
}
