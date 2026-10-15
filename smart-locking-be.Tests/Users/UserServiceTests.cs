using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Users;

public sealed class UserServiceTests
{
    [Fact]
    [Trait("Category", "USR-01")]
    public async Task GetUsersAsync_UTC01()
    {
        await ListCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "USR-01")]
    public async Task GetUsersAsync_UTC02()
    {
        await ListCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "USR-01")]
    public async Task GetUsersAsync_UTC03()
    {
        await ListCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "USR-02")]
    public async Task GetUserByIdAsync_UTC01()
    {
        await DetailCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "USR-02")]
    public async Task GetUserByIdAsync_UTC02()
    {
        await DetailCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "USR-02")]
    public async Task GetUserByIdAsync_UTC03()
    {
        await DetailCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC01()
    {
        await CreateCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC02()
    {
        await CreateCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC03()
    {
        await CreateCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC04()
    {
        await CreateCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC05()
    {
        await CreateCaseAsync(5);
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC06()
    {
        await CreateCaseAsync(6);
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC01()
    {
        await UpdateCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC02()
    {
        await UpdateCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC03()
    {
        await UpdateCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC04()
    {
        await UpdateCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC05()
    {
        await UpdateCaseAsync(5);
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC06()
    {
        await UpdateCaseAsync(6);
    }

    [Fact]
    [Trait("Category", "USR-05")]
    public async Task UpdateUserStatusAsync_UTC01()
    {
        await StatusCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "USR-05")]
    public async Task UpdateUserStatusAsync_UTC02()
    {
        await StatusCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "USR-05")]
    public async Task UpdateUserStatusAsync_UTC03()
    {
        await StatusCaseAsync(3);
    }

    [Fact(Skip = "Blocked: Excel USR-06 requires ResetCredentialsAsync; this API is absent from dev.")]
    [Trait("Category", "USR-06")]
    public async Task ResetCredentialsAsync_UTC01()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: Excel USR-06 requires ResetCredentialsAsync; this API is absent from dev.")]
    [Trait("Category", "USR-06")]
    public async Task ResetCredentialsAsync_UTC02()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: Excel USR-06 requires ResetCredentialsAsync; this API is absent from dev.")]
    [Trait("Category", "USR-06")]
    public async Task ResetCredentialsAsync_UTC03()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: Excel USR-06 requires ResetCredentialsAsync; this API is absent from dev.")]
    [Trait("Category", "USR-06")]
    public async Task ResetCredentialsAsync_UTC04()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC01()
    {
        await AssignCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC02()
    {
        await AssignCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC03()
    {
        await AssignCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC04()
    {
        await AssignCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC05()
    {
        await AssignCaseAsync(5);
    }

    [Fact]
    [Trait("Category", "USR-08")]
    public async Task RevokeOperatorScopeAsync_UTC01()
    {
        await RevokeCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "USR-08")]
    public async Task RevokeOperatorScopeAsync_UTC02()
    {
        await RevokeCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "USR-08")]
    public async Task RevokeOperatorScopeAsync_UTC03()
    {
        await RevokeCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "USR-08")]
    public async Task RevokeOperatorScopeAsync_UTC04()
    {
        await RevokeCaseAsync(4);
    }


    private static UserService Service(ApplicationDbContext db) => new(db, new Pbkdf2PasswordHashService());
    private static CreateUserRequest CreateRequest() => new(" Operator ", " NEW@example.com ", "0900000000", UserRole.LockerOperator, "NewPass123!", null, null);
    private static async Task ListCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var user = User(UserRole.LockerOperator);
        if (utc != 2)
        {
            db.Add(user);
            db.Add(User());
            await db.SaveChangesAsync();
        }
        // Keep the explicit Excel null-filter success expectation; the implementation currently dereferences null.
        var result = await Service(db).GetUsersAsync(utc == 3 ? null! : new GetUsersFilterRequest(Role: UserRole.LockerOperator, Status: UserStatus.Active));
        Assert.Equal(utc == 2 ? 0 : 1, result.TotalCount);
        if (utc == 2)
            Assert.Empty(result.Items);
        else
        {
            var item = Assert.Single(result.Items);
            Assert.Equal(user.Id, item.Id);
            Assert.Equal(UserRole.LockerOperator, item.Role);
        }
        Assert.Empty(db.AuditLogs);
    }
    private static async Task DetailCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var user = User(UserRole.LockerOperator);
        db.Add(user);
        await db.SaveChangesAsync();
        if (utc == 1)
        {
            var result = await Service(db).GetUserByIdAsync(user.Id);
            Assert.Equal(user.Id, result.Id);
            Assert.Equal(user.Email, result.Email);
            Assert.Equal(user.Role, result.Role);
            Assert.Empty(result.Assignments);
        }
        else
            await Error<KeyNotFoundException>(() => Service(db).GetUserByIdAsync(utc == 2 ? Guid.NewGuid() : Guid.Empty), "Không tìm thấy người dùng.");
        Assert.Empty(db.AuditLogs);
    }
    private static async Task CreateCaseAsync(int utc)
    {
        await using var db = CreateDb();
        Guid admin = Guid.NewGuid();
        var request = CreateRequest();
        if (utc == 5)
        {
            var existing = User();
            existing.Email = "new@example.com";
            db.Add(existing);
            await db.SaveChangesAsync();
        }
        if (utc == 2)
            request = request with
            {
                Role = UserRole.Resident
            };
        if (utc == 3)
            request = request with
            {
                FullName = ""
            };
        if (utc == 4)
            request = request with
            {
                Email = "invalid"
            };
        if (utc == 1)
        {
            var result = await Service(db).CreateUserAsync(admin, request, "127.0.0.1");
            Assert.Equal("Operator", result.FullName);
            Assert.Equal("new@example.com", result.Email);
            Assert.Equal(UserRole.LockerOperator, result.Role);
            db.ChangeTracker.Clear();
            var saved = Assert.Single(db.Users);
            Assert.Equal(result.Id, saved.Id);
            Assert.True(saved.MustChangePassword);
            Assert.Equal(UserStatus.Active, saved.Status);
            Assert.True(new Pbkdf2PasswordHashService().VerifyPassword(result.TemporaryPassword, saved.PasswordHash));
            var profile = Assert.Single(db.ResidentProfiles);
            Assert.Equal(saved.Id, profile.UserId);
            Assert.Equal("Operator", profile.FullName);
            Audit(db, "CreateUser", saved.Id, admin);
        }
        else
        {
            if (utc == 2)
                await Error<ArgumentException>(() => Service(db).CreateUserAsync(admin, request), "API quản trị chỉ được tạo tài khoản nhân viên vận hành.");
            if (utc == 3)
                await Error<ArgumentException>(() => Service(db).CreateUserAsync(admin, request), "Họ và tên không được để trống.");
            if (utc == 4)
                await Error<ArgumentException>(() => Service(db).CreateUserAsync(admin, request), "Email không hợp lệ.");
            if (utc == 5)
                await Error<InvalidOperationException>(() => Service(db).CreateUserAsync(admin, request), "Email hoặc số điện thoại đã được sử dụng.");
            if (utc == 6)
                await Assert.ThrowsAsync<NullReferenceException>(() => Service(db).CreateUserAsync(Guid.Empty, null!, ""));
            db.ChangeTracker.Clear();
            Assert.Equal(utc == 5 ? 1 : 0, db.Users.Count());
            Assert.Empty(db.ResidentProfiles);
            Assert.Empty(db.AuditLogs);
        }
    }
    private static async Task UpdateCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var user = User(utc == 3 ? UserRole.Resident : UserRole.LockerOperator);
        db.Add(user);
        Guid admin = Guid.NewGuid();
        if (utc == 5)
        {
            var duplicate = User();
            duplicate.Email = "updated@example.com";
            db.Add(duplicate);
        }
        await db.SaveChangesAsync();
        string? beforeEmail = user.Email;
        var request = new UpdateUserRequest(utc == 4 ? "" : " Updated ", "0900000000", "updated@example.com");
        if (utc == 1)
        {
            var result = await Service(db).UpdateUserAsync(admin, user.Id, request);
            Assert.Equal(user.Id, result.Id);
            Assert.Equal("Updated", result.FullName);
            Assert.Equal("updated@example.com", result.Email);
            db.ChangeTracker.Clear();
            Assert.Equal("updated@example.com", (await db.Users.SingleAsync()).Email);
            Assert.Equal("Updated", Assert.Single(db.ResidentProfiles).FullName);
            Audit(db, "UpdateUser", user.Id, admin);
        }
        else
        {
            if (utc == 2 || utc == 6)
                await Error<KeyNotFoundException>(() => Service(db).UpdateUserAsync(admin, utc == 2 ? Guid.NewGuid() : Guid.Empty, utc == 6 ? null! : request), "Không tìm thấy người dùng.");
            if (utc == 3)
                await Error<InvalidOperationException>(() => Service(db).UpdateUserAsync(admin, user.Id, request), "Thông tin cư dân phải được cập nhật qua API hồ sơ cư dân.");
            if (utc == 4)
                await Error<ArgumentException>(() => Service(db).UpdateUserAsync(admin, user.Id, request), "Họ và tên không được để trống.");
            if (utc == 5)
                await Error<InvalidOperationException>(() => Service(db).UpdateUserAsync(admin, user.Id, request), "Email đã được sử dụng bởi tài khoản khác.");
            db.ChangeTracker.Clear();
            Assert.Equal(beforeEmail, (await db.Users.SingleAsync(u => u.Id == user.Id)).Email);
            Assert.Empty(db.AuditLogs);
        }
    }
    private static async Task StatusCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var user = User(UserRole.LockerOperator);
        var other = User();
        var token = new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = "token", ExpiresAt = DateTimeOffset.UtcNow.AddDays(2) };
        var otherToken = new RefreshToken { Id = Guid.NewGuid(), UserId = other.Id, TokenHash = "other", ExpiresAt = DateTimeOffset.UtcNow.AddDays(2) };
        db.AddRange(user, other, token, otherToken);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        if (utc == 1)
        {
            var result = await Service(db).UpdateUserStatusAsync(admin, user.Id, new(UserStatus.Disabled, "reason"), "127.0.0.1");
            Assert.Equal(UserStatus.Disabled, result.Status);
            db.ChangeTracker.Clear();
            Assert.Equal(UserStatus.Disabled, (await db.Users.SingleAsync(u => u.Id == user.Id)).Status);
            var revoked = await db.RefreshTokens.SingleAsync(t => t.Id == token.Id);
            Assert.NotNull(revoked.RevokedAt);
            Assert.Equal("127.0.0.1", revoked.RevokedByIp);
            Assert.Null((await db.RefreshTokens.SingleAsync(t => t.Id == otherToken.Id)).RevokedAt);
            Audit(db, "DisableUser", user.Id, admin);
        }
        else
        {
            await Error<KeyNotFoundException>(() => Service(db).UpdateUserStatusAsync(admin, utc == 2 ? Guid.NewGuid() : Guid.Empty, utc == 3 ? null! : new(UserStatus.Locked, "reason")), "Không tìm thấy người dùng.");
            Assert.Empty(db.AuditLogs);
            Assert.Null(token.RevokedAt);
        }
    }
    private static async Task AssignCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var user = User(UserRole.LockerOperator);
        var locker = Locker();
        db.AddRange(user, locker);
        Guid admin = Guid.NewGuid();
        if (utc == 4)
            db.Add(new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = Guid.NewGuid(), LockerId = locker.Id, AssignedByUserId = admin, AssignedAt = Now });
        await db.SaveChangesAsync();
        var request = new AssignOperatorScopeRequest(utc == 3 ? Guid.Empty : locker.Id, "reason");
        if (utc == 1)
        {
            var result = await Service(db).AssignOperatorScopeAsync(admin, user.Id, request);
            Assert.Equal(locker.Id, result.LockerId);
            Assert.Equal(locker.Code, result.LockerCode);
            db.ChangeTracker.Clear();
            var saved = Assert.Single(db.OperatorAssignments);
            Assert.Equal(user.Id, saved.OperatorUserId);
            Assert.Null(saved.RevokedAt);
            Assert.Equal("reason", saved.Reason);
            Audit(db, "AssignOperatorScope", saved.Id, admin);
        }
        else
        {
            if (utc == 2 || utc == 5)
                await Error<KeyNotFoundException>(() => Service(db).AssignOperatorScopeAsync(admin, utc == 5 ? Guid.Empty : Guid.NewGuid(), utc == 5 ? null! : request), "Không tìm thấy nhân viên vận hành.");
            if (utc == 3)
                await Error<ArgumentException>(() => Service(db).AssignOperatorScopeAsync(admin, user.Id, request), "Phải chỉ định LockerId hợp lệ.");
            if (utc == 4)
                await Error<InvalidOperationException>(() => Service(db).AssignOperatorScopeAsync(admin, user.Id, request), "Tủ này đã được phân công cho một nhân viên vận hành khác.");
            db.ChangeTracker.Clear();
            Assert.Equal(utc == 4 ? 1 : 0, db.OperatorAssignments.Count());
            Assert.Empty(db.AuditLogs);
        }
    }
    private static async Task RevokeCaseAsync(int utc)
    {
        await using var db = CreateDb();
        Guid user = Guid.NewGuid();
        Guid admin = Guid.NewGuid();
        var assignment = new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = user, LockerId = Guid.NewGuid(), AssignedByUserId = admin, AssignedAt = Now.AddDays(-1), RevokedAt = utc == 3 ? Now : null };
        db.Add(assignment);
        await db.SaveChangesAsync();
        if (utc == 1)
        {
            await Service(db).RevokeOperatorScopeAsync(admin, user, assignment.Id, "reason");
            db.ChangeTracker.Clear();
            var saved = await db.OperatorAssignments.SingleAsync();
            Assert.NotNull(saved.RevokedAt);
            Assert.Equal("reason", saved.Reason);
            Audit(db, "RevokeOperatorScope", assignment.Id, admin);
        }
        else
        {
            if (utc == 3)
                await Error<InvalidOperationException>(() => Service(db).RevokeOperatorScopeAsync(admin, user, assignment.Id), "Phân công này đã bị thu hồi trước đó.");
            else
                await Error<KeyNotFoundException>(() => Service(db).RevokeOperatorScopeAsync(admin, utc == 4 ? Guid.Empty : user, utc == 4 ? Guid.Empty : Guid.NewGuid()), "Không tìm thấy bản ghi phân công.");
            db.ChangeTracker.Clear();
            Assert.Equal(utc == 3 ? Now : (DateTimeOffset?)null, (await db.OperatorAssignments.SingleAsync()).RevokedAt);
            Assert.Empty(db.AuditLogs);
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static User User(UserRole role = UserRole.Resident) => new() { Id = Guid.NewGuid(), PasswordHash = "hash", Email = $"{Guid.NewGuid():N}@example.com", PhoneNumber = "0912345678", Role = role, Status = UserStatus.Active, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
    private static Locker Locker(string code = "L01") => new() { Id = Guid.NewGuid(), Code = code, Address = "Address", RecoveryAddress = "Recovery", DeviceIdentifier = "DEVICE-" + code, OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
    private static async Task Error<T>(Func<Task> action, string message) where T : Exception
    {
        T exception = await Assert.ThrowsAsync<T>(action);
        if (exception is ArgumentException argument && argument.ParamName is not null)
            Assert.StartsWith(message, exception.Message);
        else
            Assert.Equal(message, exception.Message);
    }
    private static void Audit(ApplicationDbContext db, string action, Guid id, Guid? actor = null)
    {
        db.ChangeTracker.Clear();
        AuditLog log = Assert.Single(db.AuditLogs);
        Assert.Equal(action, log.Action);
        Assert.Equal(id, log.EntityId);
        Assert.Equal(actor, log.ActorUserId);
        Assert.Equal(AuditLogResult.Succeeded, log.Result);
    }
}
