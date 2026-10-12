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
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static (UserService Service, ApplicationDbContext DbContext) CreateTestService()
    {
        var dbContext = CreateInMemoryDbContext();
        var passwordHashService = new Pbkdf2PasswordHashService();
        var service = new UserService(dbContext, passwordHashService);
        return (service, dbContext);
    }

    [Fact]
    public async Task GetUsersAsync_ReturnsUsersWithRoleAndStatusFiltering()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var resident = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = "0900000001",
                Email = "resident@boxora.com",
                Role = UserRole.Resident,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
            };
            var opLocked = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = "0900000002",
                Email = "op.locked@boxora.com",
                Role = UserRole.LockerOperator,
                Status = UserStatus.Locked,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            var admin = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = "0900000003",
                Email = "admin@boxora.com",
                Role = UserRole.Administrator,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.Users.AddRange(resident, opLocked, admin);
            await dbContext.SaveChangesAsync();

            // All users
            var all = await service.GetUsersAsync(new GetUsersFilterRequest());
            Assert.Equal(3, all.TotalCount);

            // Filter role = LockerOperator
            var ops = await service.GetUsersAsync(new GetUsersFilterRequest(Role: UserRole.LockerOperator));
            Assert.Equal(1, ops.TotalCount);
            Assert.Equal(opLocked.Id, ops.Items[0].Id);

            // Filter status = Active
            var active = await service.GetUsersAsync(new GetUsersFilterRequest(Status: UserStatus.Active));
            Assert.Equal(2, active.TotalCount);

            // Search keyword
            var search = await service.GetUsersAsync(new GetUsersFilterRequest(Search: "resident"));
            Assert.Equal(1, search.TotalCount);
            Assert.Equal(resident.Id, search.Items[0].Id);
        }
    }

    [Fact]
    public async Task GetUserByIdAsync_WhenUserExists_ReturnsDetailResponse()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                PhoneNumber = "0911223344",
                Email = "user@boxora.com",
                Role = UserRole.Resident,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var profile = new ResidentProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FullName = "Nguyen Van A",
                DateOfBirth = new DateOnly(1990, 1, 1),
                DeliveryApprovalMode = DeliveryApprovalMode.Manual
            };
            dbContext.Users.Add(user);
            dbContext.ResidentProfiles.Add(profile);
            await dbContext.SaveChangesAsync();

            var detail = await service.GetUserByIdAsync(userId);

            Assert.NotNull(detail);
            Assert.Equal(userId, detail.Id);
            Assert.Equal("Nguyen Van A", detail.FullName);
            Assert.Equal(new DateOnly(1990, 1, 1), detail.DateOfBirth);
            Assert.Equal(DeliveryApprovalMode.Manual, detail.DeliveryApprovalMode);
        }
    }

    [Fact]
    public async Task GetUserByIdAsync_WhenNotFound_ThrowsKeyNotFoundException()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetUserByIdAsync(Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task CreateUserAsync_WhenOperator_CreatesWithTemporaryPasswordAndAuditLog()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var request = new CreateUserRequest(
                "Le Van Operator",
                "op.new@boxora.com",
                "0933445566",
                UserRole.LockerOperator,
                null, // Random temporary password
                null, null
            );

            var response = await service.CreateUserAsync(adminId, request, "10.0.0.1");

            Assert.NotNull(response);
            Assert.Equal("op.new@boxora.com", response.Email);
            Assert.Equal("Le Van Operator", response.FullName);
            Assert.NotEmpty(response.TemporaryPassword);
            Assert.Equal(UserStatus.Active, response.Status);
            Assert.Equal(UserRole.LockerOperator, response.Role);

            var user = await dbContext.Users.FirstAsync(u => u.Id == response.Id);
            Assert.Equal(UserRole.LockerOperator, user.Role);
            Assert.True(user.MustChangePassword);

            var auditLog = await dbContext.AuditLogs.FirstOrDefaultAsync(l => l.EntityId == response.Id && l.Action == "CreateUser");
            Assert.NotNull(auditLog);
            Assert.Equal(adminId, auditLog.ActorUserId);
        }
    }

    [Fact]
    public async Task CreateUserAsync_WhenDuplicateEmail_ThrowsInvalidOperationException()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var existingUser = new User
            {
                Id = Guid.NewGuid(),
                Email = "existing@boxora.com",
                Role = UserRole.LockerOperator,
                Status = UserStatus.Active
            };
            dbContext.Users.Add(existingUser);
            await dbContext.SaveChangesAsync();

            var request = new CreateUserRequest("Name", "existing@boxora.com", null, UserRole.LockerOperator, null, null, null);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateUserAsync(adminId, request));
        }
    }

    [Fact]
    public async Task CreateUserAsync_WhenRoleIsNotOperator_ThrowsArgumentException()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var request = new CreateUserRequest(
                "Resident",
                "resident.new@boxora.com",
                null,
                UserRole.Resident,
                null,
                null,
                null);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.CreateUserAsync(Guid.NewGuid(), request));
        }
    }

    [Fact]
    public async Task UpdateUserAsync_UpdatesProfileAndContactInfo()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Email = "old@boxora.com",
                PhoneNumber = "0900000001",
                Role = UserRole.LockerOperator,
                Status = UserStatus.Active
            };
            var profile = new ResidentProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FullName = "Old Name"
            };
            dbContext.Users.Add(user);
            dbContext.ResidentProfiles.Add(profile);
            await dbContext.SaveChangesAsync();

            var request = new UpdateUserRequest("New Name", "0900000099", "new@boxora.com");
            var result = await service.UpdateUserAsync(adminId, userId, request, "127.0.0.1");

            Assert.Equal("New Name", result.FullName);
            Assert.Equal("new@boxora.com", result.Email);
            Assert.Equal("0900000099", result.PhoneNumber);

            var auditLog = await dbContext.AuditLogs.FirstOrDefaultAsync(l => l.EntityId == userId && l.Action == "UpdateUser");
            Assert.NotNull(auditLog);
        }
    }

    [Fact]
    public async Task UpdateUserAsync_WhenResident_RejectsAdminProfileEdit()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var resident = new User
            {
                Id = Guid.NewGuid(),
                Email = "resident@boxora.com",
                Role = UserRole.Resident,
                Status = UserStatus.Active
            };
            dbContext.Users.Add(resident);
            await dbContext.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateUserAsync(
                Guid.NewGuid(),
                resident.Id,
                new UpdateUserRequest("Changed", null, "changed@boxora.com")));
        }
    }

    [Fact]
    public async Task UpdateUserStatusAsync_WhenLocked_UpdatesStatusAndRevokesRefreshTokens()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Email = "user@boxora.com",
                Role = UserRole.Resident,
                Status = UserStatus.Active
            };
            var token = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = "tokenhash",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Users.Add(user);
            dbContext.RefreshTokens.Add(token);
            await dbContext.SaveChangesAsync();

            var request = new UpdateUserStatusRequest(UserStatus.Locked, "Tài khoản có dấu hiệu bị hack");
            var result = await service.UpdateUserStatusAsync(adminId, userId, request, "127.0.0.1");

            Assert.Equal(UserStatus.Locked, result.Status);

            var updatedToken = await dbContext.RefreshTokens.FirstAsync(t => t.Id == token.Id);
            Assert.NotNull(updatedToken.RevokedAt);

            var auditLog = await dbContext.AuditLogs.FirstOrDefaultAsync(l => l.EntityId == userId && l.Action == "LockUser");
            Assert.NotNull(auditLog);
            Assert.Contains("Tài khoản có dấu hiệu bị hack", auditLog.Details);
        }
    }

    [Fact]
    public async Task AssignOperatorScopeAsync_WhenValid_CreatesAssignment()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var opId = Guid.NewGuid();
            var lockerId = Guid.NewGuid();

            var op = new User
            {
                Id = opId,
                Email = "op@boxora.com",
                Role = UserRole.LockerOperator,
                Status = UserStatus.Active
            };
            var locker = new Locker
            {
                Id = lockerId,
                Code = "L01",
                Address = "Khu Công Nghệ Cao",
                RecoveryAddress = "Khu Công Nghệ Cao",
                DeviceIdentifier = "DEVICE-01"
            };
            dbContext.Users.Add(op);
            dbContext.Lockers.Add(locker);
            await dbContext.SaveChangesAsync();

            var request = new AssignOperatorScopeRequest(lockerId, "Phụ trách locker L01");
            var result = await service.AssignOperatorScopeAsync(adminId, opId, request, "127.0.0.1");

            Assert.NotNull(result);
            Assert.Equal(lockerId, result.LockerId);
            Assert.Equal("L01", result.LockerCode);

            var persisted = await dbContext.OperatorAssignments.FirstAsync(a => a.Id == result.Id);
            Assert.Equal(opId, persisted.OperatorUserId);
            Assert.Equal(adminId, persisted.AssignedByUserId);
        }
    }

    [Fact]
    public async Task AssignOperatorScopeAsync_WhenLockerAssignedToAnotherOperator_ThrowsInvalidOperationException()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var assignedOperatorId = Guid.NewGuid();
            var newOperatorId = Guid.NewGuid();
            var lockerId = Guid.NewGuid();

            dbContext.Users.AddRange(
                new User
                {
                    Id = assignedOperatorId,
                    Email = "assigned.op@boxora.com",
                    Role = UserRole.LockerOperator,
                    Status = UserStatus.Active
                },
                new User
                {
                    Id = newOperatorId,
                    Email = "new.op@boxora.com",
                    Role = UserRole.LockerOperator,
                    Status = UserStatus.Active
                });
            dbContext.OperatorAssignments.Add(new OperatorAssignment
            {
                Id = Guid.NewGuid(),
                OperatorUserId = assignedOperatorId,
                LockerId = lockerId,
                AssignedByUserId = adminId,
                AssignedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();

            var request = new AssignOperatorScopeRequest(lockerId, "Phân công lại tủ");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AssignOperatorScopeAsync(adminId, newOperatorId, request));

            Assert.Equal("Tủ này đã được phân công cho một nhân viên vận hành khác.", exception.Message);
            Assert.Equal(1, await dbContext.OperatorAssignments.CountAsync(a => a.LockerId == lockerId));
        }
    }

    [Fact]
    public async Task RevokeOperatorScopeAsync_UpdatesRevokedAt()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var opId = Guid.NewGuid();
            var assignmentId = Guid.NewGuid();

            var op = new User
            {
                Id = opId,
                Email = "op@boxora.com",
                Role = UserRole.LockerOperator,
                Status = UserStatus.Active
            };
            var assignment = new OperatorAssignment
            {
                Id = assignmentId,
                OperatorUserId = opId,
                LockerId = Guid.NewGuid(),
                AssignedByUserId = adminId,
                AssignedAt = DateTimeOffset.UtcNow.AddDays(-5)
            };
            dbContext.Users.Add(op);
            dbContext.OperatorAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();

            await service.RevokeOperatorScopeAsync(adminId, opId, assignmentId, "Chuyển công tác", "127.0.0.1");

            var updated = await dbContext.OperatorAssignments.FirstAsync(a => a.Id == assignmentId);
            Assert.NotNull(updated.RevokedAt);
            Assert.Equal("Chuyển công tác", updated.Reason);
        }
    }

    #region Standardized Matrix Tests
    [Fact]
    [Trait("Category", "USR-01")]
    public async Task GetUsersAsync_UTC01()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var paged = await service.GetUsersAsync(new GetUsersFilterRequest());
            Assert.NotNull(paged);
        }
    }

    [Fact]
    [Trait("Category", "USR-02")]
    public async Task GetUserByIdAsync_UTC01()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var user = new User { Id = Guid.NewGuid(), PhoneNumber = "0911111111", Email = "u1@boxora.com", Role = UserRole.Resident, Status = UserStatus.Active };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();

            var detail = await service.GetUserByIdAsync(user.Id);
            Assert.Equal(user.Id, detail.Id);
        }
    }

    [Fact]
    [Trait("Category", "USR-02")]
    public async Task GetUserByIdAsync_UTC02()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetUserByIdAsync(Guid.NewGuid()));
        }
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC01()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var req = new CreateUserRequest("Nguyen Van Test", "create1@boxora.com", "0922222222", UserRole.LockerOperator, "Password@123", null, null);
            var created = await service.CreateUserAsync(adminId, req, "127.0.0.1");

            Assert.Equal("0922222222", created.PhoneNumber);
        }
    }

    [Fact]
    [Trait("Category", "USR-03")]
    public async Task CreateUserAsync_UTC02()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var user = new User { Id = Guid.NewGuid(), PhoneNumber = "0933333333", Email = "dup@boxora.com", Role = UserRole.LockerOperator, Status = UserStatus.Active };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();

            var req = new CreateUserRequest("Duplicate", "dup@boxora.com", "0988888888", UserRole.LockerOperator, "Password@123", null, null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateUserAsync(adminId, req, "127.0.0.1"));
        }
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC01()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var user = new User { Id = Guid.NewGuid(), PhoneNumber = "0944444444", Email = "upd@boxora.com", Role = UserRole.LockerOperator, Status = UserStatus.Active };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();

            var req = new UpdateUserRequest("Nguyen New", "0944444444", "upd.new@boxora.com");
            var updated = await service.UpdateUserAsync(adminId, user.Id, req, "127.0.0.1");

            Assert.Equal("upd.new@boxora.com", updated.Email);
        }
    }

    [Fact]
    [Trait("Category", "USR-04")]
    public async Task UpdateUserAsync_UTC02()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                service.UpdateUserAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdateUserRequest("0912345678", "a@b.com", "Name"), "127.0.0.1"));
        }
    }

    [Fact]
    [Trait("Category", "USR-05")]
    public async Task UpdateUserStatusAsync_UTC01()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var user = new User { Id = Guid.NewGuid(), PhoneNumber = "0955555555", Email = "stat@boxora.com", Role = UserRole.Resident, Status = UserStatus.Active };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();

            var updated = await service.UpdateUserStatusAsync(adminId, user.Id, new UpdateUserStatusRequest(UserStatus.Locked, "Tam khoa"), "127.0.0.1");
            Assert.Equal(UserStatus.Locked, updated.Status);
        }
    }

    [Fact]
    [Trait("Category", "USR-05")]
    public async Task UpdateUserStatusAsync_UTC02()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                service.UpdateUserStatusAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdateUserStatusRequest(UserStatus.Locked, "Reason"), "127.0.0.1"));
        }
    }

    [Fact]
    [Trait("Category", "USR-06")]
    public async Task ResetUserCredentialsAsync_UTC01()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var op = new User { Id = Guid.NewGuid(), PhoneNumber = "0966666666", Email = "cred@boxora.com", Role = UserRole.LockerOperator, Status = UserStatus.Active };
            dbContext.Users.Add(op);
            await dbContext.SaveChangesAsync();

            var result = await service.ResetCredentialsAsync(adminId, op.Id, "127.0.0.1");
            Assert.NotNull(result.TemporaryPassword);
        }
    }

    [Fact]
    [Trait("Category", "USR-06")]
    public async Task ResetUserCredentialsAsync_UTC02()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                service.ResetCredentialsAsync(Guid.NewGuid(), Guid.NewGuid(), "127.0.0.1"));
        }
    }

    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC01()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var op = new User { Id = Guid.NewGuid(), PhoneNumber = "0977777777", Email = "op1@boxora.com", Role = UserRole.LockerOperator, Status = UserStatus.Active };
            var locker = new Locker { Id = Guid.NewGuid(), Code = "LK-OP-01", Address = "A", RecoveryAddress = "R", DeviceIdentifier = "D1" };
            dbContext.AddRange(op, locker);
            await dbContext.SaveChangesAsync();

            var result = await service.AssignOperatorScopeAsync(adminId, op.Id, new AssignOperatorScopeRequest(locker.Id, "Phan cong"));
            Assert.Equal(locker.Id, result.LockerId);
        }
    }

    [Fact]
    [Trait("Category", "USR-07")]
    public async Task AssignOperatorScopeAsync_UTC02()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                service.AssignOperatorScopeAsync(Guid.NewGuid(), Guid.NewGuid(), new AssignOperatorScopeRequest(Guid.NewGuid(), "Phan cong")));
        }
    }

    [Fact]
    [Trait("Category", "USR-08")]
    public async Task RevokeOperatorScopeAsync_UTC01()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var opId = Guid.NewGuid();
            var assignmentId = Guid.NewGuid();
            var op = new User { Id = opId, PhoneNumber = "0988888888", Email = "op2@boxora.com", Role = UserRole.LockerOperator, Status = UserStatus.Active };
            var assignment = new OperatorAssignment { Id = assignmentId, OperatorUserId = opId, LockerId = Guid.NewGuid(), AssignedByUserId = adminId, AssignedAt = DateTimeOffset.UtcNow };
            dbContext.AddRange(op, assignment);
            await dbContext.SaveChangesAsync();

            await service.RevokeOperatorScopeAsync(adminId, opId, assignmentId, "Thu hoi", "127.0.0.1");
            var record = await dbContext.OperatorAssignments.FindAsync(assignmentId);
            Assert.NotNull(record!.RevokedAt);
        }
    }

    [Fact]
    [Trait("Category", "USR-08")]
    public async Task RevokeOperatorScopeAsync_UTC02()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                service.RevokeOperatorScopeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Reason", "127.0.0.1"));
        }
    }
    #endregion
}
