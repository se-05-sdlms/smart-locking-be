using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Residents;

public sealed class ResidentServiceTests
{
    [Fact]
    [Trait("Category", "RES-01")]
    public async Task GetProfileAsync_UTC01()
    {
        await using var db = CreateDb();
        var service = new ResidentService(db);
        var user = User();
        var profile = new ResidentProfile { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Resident", DeliveryApprovalMode = DeliveryApprovalMode.Manual };
        db.AddRange(user, profile);
        await db.SaveChangesAsync();
        var result = await service.GetProfileAsync(user.Id);
        Assert.Equal(profile.Id, result.Id);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal("Resident", result.FullName);
        Assert.Equal(user.Email, result.Email);
        Assert.Equal(DeliveryApprovalMode.Manual, result.DeliveryApprovalMode);
        Assert.Single(db.ResidentProfiles);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "RES-01")]
    public async Task GetProfileAsync_UTC02()
    {
        await using var db = CreateDb();
        var service = new ResidentService(db);
        var user = User();
        var profile = new ResidentProfile { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Resident", DeliveryApprovalMode = DeliveryApprovalMode.Manual };
        await Error<KeyNotFoundException>(() => service.GetProfileAsync(user.Id), "Không tìm thấy thông tin tài khoản.");
        Assert.Empty(db.ResidentProfiles);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "RES-01")]
    public async Task GetProfileAsync_UTC03()
    {
        await using var db = CreateDb();
        var service = new ResidentService(db);
        var user = User();
        var profile = new ResidentProfile { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Resident", DeliveryApprovalMode = DeliveryApprovalMode.Manual };
        // Excel expects success for an existing boundary-ID user. Disable key generation only in this test model.
        await using var boundaryDb = new BoundaryDb();
        user.Id = Guid.Empty;
        profile.UserId = Guid.Empty;
        boundaryDb.AddRange(user, profile);
        await boundaryDb.SaveChangesAsync();
        var result = await new ResidentService(boundaryDb).GetProfileAsync(Guid.Empty);
        Assert.Equal(Guid.Empty, result.UserId);
        Assert.Equal(profile.Id, result.Id);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC01()
    {
        await using var db = CreateDb();
        var user = User();
        var profile = new ResidentProfile { Id = Guid.NewGuid(), User = user, UserId = user.Id, FullName = "Before" };
        db.AddRange(user, profile);
        await db.SaveChangesAsync();
        var service = new ResidentService(db);
        var request = new UpdateResidentProfileRequest(" Updated ", new DateOnly(2000, 1, 1), " https://example.com/avatar.jpg ");
        var result = await service.UpdateProfileAsync(user.Id, request);
        Assert.Equal("Updated", result.FullName);
        db.ChangeTracker.Clear();
        var saved = await db.ResidentProfiles.SingleAsync();
        Assert.Equal("Updated", saved.FullName);
        Assert.Equal(request.DateOfBirth, saved.DateOfBirth);
        Assert.Equal("https://example.com/avatar.jpg", saved.AvatarUrl);
        Assert.Equal(user.Id, saved.UserId);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC02()
    {
        await using var db = CreateDb();
        var user = User();
        var profile = new ResidentProfile { Id = Guid.NewGuid(), User = user, UserId = user.Id, FullName = "Before" };
        db.AddRange(user, profile);
        await db.SaveChangesAsync();
        var service = new ResidentService(db);
        var request = new UpdateResidentProfileRequest(" Updated ", new DateOnly(2000, 1, 1), " https://example.com/avatar.jpg ");
        await Error<ArgumentException>(() => service.UpdateProfileAsync(user.Id, request with { FullName = "" }), "Họ và tên không được để trống.");
        db.ChangeTracker.Clear();
        var saved = await db.ResidentProfiles.SingleAsync();
        Assert.Equal("Before", saved.FullName);
        Assert.Null(saved.AvatarUrl);
        Assert.Null(saved.DateOfBirth);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC03()
    {
        await using var db = CreateDb();
        var user = User();
        var profile = new ResidentProfile { Id = Guid.NewGuid(), User = user, UserId = user.Id, FullName = "Before" };
        db.AddRange(user, profile);
        await db.SaveChangesAsync();
        var service = new ResidentService(db);
        var request = new UpdateResidentProfileRequest(" Updated ", new DateOnly(2000, 1, 1), " https://example.com/avatar.jpg ");
        await Error<ArgumentException>(() => service.UpdateProfileAsync(user.Id, request with { FullName = new string('A', 151) }), "Họ và tên không được vượt quá 150 ký tự.");
        db.ChangeTracker.Clear();
        var saved = await db.ResidentProfiles.SingleAsync();
        Assert.Equal("Before", saved.FullName);
        Assert.Null(saved.AvatarUrl);
        Assert.Null(saved.DateOfBirth);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC04()
    {
        await using var db = CreateDb();
        var user = User();
        var profile = new ResidentProfile { Id = Guid.NewGuid(), User = user, UserId = user.Id, FullName = "Before" };
        db.AddRange(user, profile);
        await db.SaveChangesAsync();
        var service = new ResidentService(db);
        var request = new UpdateResidentProfileRequest(" Updated ", new DateOnly(2000, 1, 1), " https://example.com/avatar.jpg ");
        await Error<ArgumentException>(() => service.UpdateProfileAsync(user.Id, request with { DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2) }), "Ngày sinh không được ở trong tương lai.");
        db.ChangeTracker.Clear();
        var saved = await db.ResidentProfiles.SingleAsync();
        Assert.Equal("Before", saved.FullName);
        Assert.Null(saved.AvatarUrl);
        Assert.Null(saved.DateOfBirth);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC05()
    {
        await using var db = CreateDb();
        var user = User();
        var profile = new ResidentProfile { Id = Guid.NewGuid(), User = user, UserId = user.Id, FullName = "Before" };
        db.AddRange(user, profile);
        await db.SaveChangesAsync();
        var service = new ResidentService(db);
        var request = new UpdateResidentProfileRequest(" Updated ", new DateOnly(2000, 1, 1), " https://example.com/avatar.jpg ");
        await Error<ArgumentException>(() => service.UpdateProfileAsync(user.Id, request with { AvatarUrl = new string('A', 2049) }), "Đường dẫn ảnh đại diện không được vượt quá 2048 ký tự.");
        db.ChangeTracker.Clear();
        var saved = await db.ResidentProfiles.SingleAsync();
        Assert.Equal("Before", saved.FullName);
        Assert.Null(saved.AvatarUrl);
        Assert.Null(saved.DateOfBirth);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "RES-02")]
    public async Task UpdateProfileAsync_UTC06()
    {
        await using var db = CreateDb();
        var user = User();
        var profile = new ResidentProfile { Id = Guid.NewGuid(), User = user, UserId = user.Id, FullName = "Before" };
        db.AddRange(user, profile);
        await db.SaveChangesAsync();
        var service = new ResidentService(db);
        var request = new UpdateResidentProfileRequest(" Updated ", new DateOnly(2000, 1, 1), " https://example.com/avatar.jpg ");
        await Assert.ThrowsAsync<NullReferenceException>(() => service.UpdateProfileAsync(Guid.Empty, null!));
        db.ChangeTracker.Clear();
        Assert.Equal("Before", (await db.ResidentProfiles.SingleAsync()).FullName);
        Assert.Empty(db.AuditLogs);
    }

    private sealed class BoundaryDb : ApplicationDbContext
    {
        public BoundaryDb() : base(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<User>().Property(u => u.Id).ValueGeneratedNever();
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static User User(UserRole role = UserRole.Resident) => new() { Id = Guid.NewGuid(), PasswordHash = "hash", Email = $"{Guid.NewGuid():N}@example.com", PhoneNumber = "0912345678", Role = role, Status = UserStatus.Active, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
    private static async Task Error<T>(Func<Task> action, string message) where T : Exception
    {
        T exception = await Assert.ThrowsAsync<T>(action);
        if (exception is ArgumentException argument && argument.ParamName is not null)
            Assert.StartsWith(message, exception.Message);
        else
            Assert.Equal(message, exception.Message);
    }
}
