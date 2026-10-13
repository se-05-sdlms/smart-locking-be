using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Users;

public sealed class UserServiceTests
{
    #region USR-01: GetUsersAsync
    [Fact]
    [Trait("Category", "USR-01")]
    public async Task GetUsersAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        PagedResult<UserListItemResponse> result = await service.GetUsersAsync(new GetUsersFilterRequest());

        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 1);
        Assert.Contains(result.Items, u => u.Id == user.Id);
    }

    [Fact]
    [Trait("Category", "USR-01")]
    public async Task GetUsersAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        PagedResult<UserListItemResponse> result = await service.GetUsersAsync(new GetUsersFilterRequest(Search: "non-existent-search-key"));

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    [Trait("Category", "USR-01")]
    public async Task GetUsersAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        PagedResult<UserListItemResponse> result = await service.GetUsersAsync(new GetUsersFilterRequest());
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
    }
    #endregion

    #region USR-02: GetUserByIdAsync
    [Fact]
    [Trait("Category", "USR-02")]
    public async Task GetUserByIdAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        UserDetailResponse detail = await service.GetUserByIdAsync(user.Id);

        Assert.NotNull(detail);
        Assert.Equal(user.Id, detail.Id);
    }

    [Fact]
    [Trait("Category", "USR-02")]
    public async Task GetUserByIdAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetUserByIdAsync(Guid.NewGuid()));
        Assert.Contains("Không tìm thấy người dùng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-02")]
    public async Task GetUserByIdAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetUserByIdAsync(Guid.Empty));
        Assert.Contains("Không tìm thấy người dùng.", ex.Message);
    }
    #endregion

    #region USR-03: CreateUserAsync
    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        Guid adminId = Guid.NewGuid();

        CreateUserResponse response = await service.CreateUserAsync(
            adminId,
            new CreateUserRequest("Nguyen Operator", "op1@boxora.com", "0901234567", UserRole.LockerOperator, null, null, null));

        Assert.NotNull(response);
        Assert.Equal("op1@boxora.com", response.Email);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "CreateUser" && a.Result == AuditLogResult.Succeeded);
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateUserAsync(
                Guid.NewGuid(),
                new CreateUserRequest("Nguyen Resident", "res@boxora.com", "0901234567", UserRole.Resident, null, null, null)));

        Assert.Contains("API quản trị chỉ được tạo tài khoản nhân viên vận hành.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateUserAsync(
                Guid.NewGuid(),
                new CreateUserRequest("", "op@boxora.com", "0901234567", UserRole.LockerOperator, null, null, null)));

        Assert.Contains("Họ và tên không được để trống.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateUserAsync(
                Guid.NewGuid(),
                new CreateUserRequest("Nguyen Op", "invalid-email", "0901234567", UserRole.LockerOperator, null, null, null)));

        Assert.Contains("Email không hợp lệ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var existing = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0901234567");
        dbContext.Users.Add(existing);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateUserAsync(
                Guid.NewGuid(),
                new CreateUserRequest("Nguyen Op New", "op@boxora.com", "0909999999", UserRole.LockerOperator, null, null, null)));

        Assert.Contains("Email hoặc số điện thoại đã được sử dụng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        // LockerId does not exist
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.CreateUserAsync(
                Guid.NewGuid(),
                new CreateUserRequest("Nguyen Op", "op2@boxora.com", "0908888888", UserRole.LockerOperator, null, Guid.NewGuid(), null)));
    }
    #endregion

    #region USR-04: UpdateUserAsync
    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var op = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        dbContext.Users.Add(op);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        UserDetailResponse updated = await service.UpdateUserAsync(
            Guid.NewGuid(),
            op.Id,
            new UpdateUserRequest("Nguyen Operator Updated", "0900000002", "op.updated@boxora.com"));

        Assert.Equal("Nguyen Operator Updated", updated.FullName);
        Assert.Equal("op.updated@boxora.com", updated.Email);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "UpdateUser" && a.Result == AuditLogResult.Succeeded);
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateUserAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdateUserRequest("Name", null, null)));

        Assert.Contains("Không tìm thấy người dùng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var resident = CreateUser(UserRole.Resident, UserStatus.Active, "res@boxora.com", "0900000001");
        dbContext.Users.Add(resident);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateUserAsync(Guid.NewGuid(), resident.Id, new UpdateUserRequest("New Name", null, null)));

        Assert.Contains("Thông tin cư dân phải được cập nhật qua API hồ sơ cư dân.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var op = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        dbContext.Users.Add(op);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateUserAsync(Guid.NewGuid(), op.Id, new UpdateUserRequest("", null, null)));

        Assert.Contains("Họ và tên không được để trống.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var op1 = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op1@boxora.com", "0900000001");
        var op2 = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op2@boxora.com", "0900000002");
        dbContext.Users.AddRange(op1, op2);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateUserAsync(Guid.NewGuid(), op2.Id, new UpdateUserRequest("Name", null, "op1@boxora.com")));

        Assert.Contains("Email đã được sử dụng bởi tài khoản khác.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateUserAsync(Guid.NewGuid(), Guid.Empty, new UpdateUserRequest("Name", null, null)));

        Assert.Contains("Không tìm thấy người dùng.", ex.Message);
    }
    #endregion

    #region USR-05: UpdateUserStatusAsync
    [Fact]
    [Trait("Category", "USR-05")]
    public async Task UpdateUserStatusAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        UserDetailResponse response = await service.UpdateUserStatusAsync(
            Guid.NewGuid(),
            user.Id,
            new UpdateUserStatusRequest(UserStatus.Locked, "Vi phạm quy định"));

        Assert.Equal(UserStatus.Locked, response.Status);
    }

    [Fact]
    [Trait("Category", "USR-05")]
    public async Task UpdateUserStatusAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateUserStatusAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdateUserStatusRequest(UserStatus.Locked, "Reason")));

        Assert.Contains("Không tìm thấy người dùng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-05")]
    public async Task UpdateUserStatusAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateUserStatusAsync(Guid.NewGuid(), Guid.Empty, new UpdateUserStatusRequest(UserStatus.Locked, "Reason")));

        Assert.Contains("Không tìm thấy người dùng.", ex.Message);
    }
    #endregion

    #region USR-06: ResetCredentialsAsync
    [Fact]
    [Trait("Category", "USR-06")]
    public async Task ResetCredentialsAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var op = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        dbContext.Users.Add(op);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        ResetUserCredentialsResponse response = await service.ResetCredentialsAsync(Guid.NewGuid(), op.Id);

        Assert.NotNull(response.TemporaryPassword);
        Assert.True(response.MustChangePassword);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "ResetUserCredentials" && a.Result == AuditLogResult.Succeeded);
    }

    [Fact]
    [Trait("Category", "USR-06")]
    public async Task ResetCredentialsAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ResetCredentialsAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Contains("Không tìm thấy người dùng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-06")]
    public async Task ResetCredentialsAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var resident = CreateUser(UserRole.Resident, UserStatus.Active, "res@boxora.com", "0900000001");
        dbContext.Users.Add(resident);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetCredentialsAsync(Guid.NewGuid(), resident.Id));

        Assert.Contains("Chỉ được đặt lại thông tin đăng nhập cho nhân viên vận hành.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-06")]
    public async Task ResetCredentialsAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ResetCredentialsAsync(Guid.NewGuid(), Guid.Empty));

        Assert.Contains("Không tìm thấy người dùng.", ex.Message);
    }
    #endregion

    #region USR-07: AssignOperatorScopeAsync
    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var op = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        var locker = CreateLocker();
        dbContext.AddRange(op, locker);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        OperatorAssignmentResponse response = await service.AssignOperatorScopeAsync(
            Guid.NewGuid(),
            op.Id,
            new AssignOperatorScopeRequest(locker.Id, "Phân công định kỳ"));

        Assert.NotNull(response);
        Assert.Equal(locker.Id, response.LockerId);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "AssignOperatorScope" && a.Result == AuditLogResult.Succeeded);
    }

    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker();
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.AssignOperatorScopeAsync(Guid.NewGuid(), Guid.NewGuid(), new AssignOperatorScopeRequest(locker.Id, "Reason")));

        Assert.Contains("Không tìm thấy nhân viên vận hành.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var op = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        dbContext.Add(op);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AssignOperatorScopeAsync(Guid.NewGuid(), op.Id, new AssignOperatorScopeRequest(Guid.Empty, "Reason")));

        Assert.Contains("Phải chỉ định LockerId hợp lệ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var op1 = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op1@boxora.com", "0900000001");
        var op2 = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op2@boxora.com", "0900000002");
        var locker = CreateLocker();
        var existingAssignment = new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = op1.Id,
            LockerId = locker.Id,
            AssignedByUserId = Guid.NewGuid(),
            AssignedAt = DateTimeOffset.UtcNow,
            Reason = "Active assignment"
        };
        dbContext.AddRange(op1, op2, locker, existingAssignment);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignOperatorScopeAsync(Guid.NewGuid(), op2.Id, new AssignOperatorScopeRequest(locker.Id, "Reason")));

        Assert.Contains("Tủ này đã được phân công cho một nhân viên vận hành khác.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.AssignOperatorScopeAsync(Guid.NewGuid(), Guid.Empty, new AssignOperatorScopeRequest(Guid.NewGuid(), "Reason")));

        Assert.Contains("Không tìm thấy nhân viên vận hành.", ex.Message);
    }
    #endregion

    #region USR-08: RevokeOperatorScopeAsync
    [Fact]
    [Trait("Category", "USR-08")]
    public async Task RevokeOperatorScopeAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var op = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        var locker = CreateLocker();
        var assignment = new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = op.Id,
            LockerId = locker.Id,
            AssignedByUserId = Guid.NewGuid(),
            AssignedAt = DateTimeOffset.UtcNow,
            Reason = "Active assignment"
        };
        dbContext.AddRange(op, locker, assignment);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        await service.RevokeOperatorScopeAsync(Guid.NewGuid(), op.Id, assignment.Id, "Thu hồi");

        Assert.NotNull(assignment.RevokedAt);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "RevokeOperatorScope" && a.Result == AuditLogResult.Succeeded);
    }

    [Fact]
    [Trait("Category", "USR-08")]
    public async Task RevokeOperatorScopeAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var op = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        dbContext.Add(op);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.RevokeOperatorScopeAsync(Guid.NewGuid(), op.Id, Guid.NewGuid(), "Reason"));

        Assert.Contains("Không tìm thấy bản ghi phân công.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-08")]
    public async Task RevokeOperatorScopeAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var op = CreateUser(UserRole.LockerOperator, UserStatus.Active, "op@boxora.com", "0900000001");
        var locker = CreateLocker();
        var assignment = new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = op.Id,
            LockerId = locker.Id,
            AssignedByUserId = Guid.NewGuid(),
            AssignedAt = DateTimeOffset.UtcNow,
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            Reason = "Revoked assignment"
        };
        dbContext.AddRange(op, locker, assignment);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RevokeOperatorScopeAsync(Guid.NewGuid(), op.Id, assignment.Id, "Reason"));

        Assert.Contains("Phân công này đã bị thu hồi trước đó.", ex.Message);
    }

    [Fact]
    [Trait("Category", "USR-08")]
    public async Task RevokeOperatorScopeAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.RevokeOperatorScopeAsync(Guid.NewGuid(), Guid.Empty, Guid.Empty, "Reason"));

        Assert.Contains("Không tìm thấy bản ghi phân công.", ex.Message);
    }
    #endregion

    #region Helpers
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static UserService CreateService(ApplicationDbContext dbContext) =>
        new(dbContext, new Pbkdf2PasswordHashService());

    private static User CreateUser(UserRole role, UserStatus status, string email, string phone) =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            PhoneNumber = phone,
            PasswordHash = "hash",
            Role = role,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ResidentProfile = new ResidentProfile
            {
                Id = Guid.NewGuid(),
                FullName = "Nguyen Test",
                DeliveryApprovalMode = DeliveryApprovalMode.Manual,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

    private static Locker CreateLocker() =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = $"LKR-{Guid.NewGuid():N}"[..10],
            Address = "Test Address",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    #endregion
}
