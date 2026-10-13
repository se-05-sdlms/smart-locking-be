using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

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

    #region RES-01: ResidentService.GetProfileAsync
    [Fact]
    [Trait("Category", "RES-01")]
    public async Task GetProfileAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "resident@example.com",
            PhoneNumber = "0912345678",
            Role = UserRole.Resident,
            Status = UserStatus.Active,
            ResidentProfile = new ResidentProfile
            {
                Id = Guid.NewGuid(),
                FullName = "Nguyen Van A",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = new ResidentService(dbContext);
        var result = await service.GetProfileAsync(user.Id);

        Assert.NotNull(result);
        Assert.Equal("Nguyen Van A", result.FullName);
    }

    [Fact]
    [Trait("Category", "RES-01")]
    public async Task GetProfileAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new ResidentService(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetProfileAsync(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "RES-01")]
    public async Task GetProfileAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new ResidentService(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetProfileAsync(Guid.Empty));
    }
    #endregion

    #region RES-02: ResidentService.UpdateProfileAsync
    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "resident@example.com",
            PhoneNumber = "0912345678",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = new ResidentService(dbContext);
        var req = new UpdateResidentProfileRequest("Nguyen Van B", null, null, null);
        var result = await service.UpdateProfileAsync(user.Id, req);

        Assert.Equal("Nguyen Van B", result.FullName);
    }

    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = new ResidentService(dbContext);
        var req = new UpdateResidentProfileRequest("   ", null, null, null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateProfileAsync(user.Id, req));
        Assert.Equal("Họ và tên không được để trống. (Parameter 'FullName')", ex.Message);
    }

    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = new ResidentService(dbContext);
        string longName = new string('a', 151);
        var req = new UpdateResidentProfileRequest(longName, null, null, null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateProfileAsync(user.Id, req));
        Assert.Equal("Họ và tên không được vượt quá 150 ký tự. (Parameter 'FullName')", ex.Message);
    }

    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC04()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = new ResidentService(dbContext);
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var req = new UpdateResidentProfileRequest("Valid Name", futureDate, null, null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateProfileAsync(user.Id, req));
        Assert.Equal("Ngày sinh không được ở trong tương lai. (Parameter 'DateOfBirth')", ex.Message);
    }

    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC05()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = new ResidentService(dbContext);
        string longUrl = "https://example.com/" + new string('a', 2050);
        var req = new UpdateResidentProfileRequest("Valid Name", null, longUrl, null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateProfileAsync(user.Id, req));
        Assert.Equal("Đường dẫn ảnh đại diện không được vượt quá 2048 ký tự. (Parameter 'AvatarUrl')", ex.Message);
    }

    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC06()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new ResidentService(dbContext);

        await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateProfileAsync(Guid.Empty, null!));
    }
    #endregion
}
