using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

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
        var tokenHashService = new Sha256TokenHashService();
        var service = new UserService(dbContext, passwordHashService, tokenHashService);
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
                DeliveryApprovalMode = DeliveryApprovalMode.Manual,
                PersonalQrTokenHash = "hash",
                PersonalQrIssuedAt = DateTimeOffset.UtcNow
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
                Role = UserRole.Resident,
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
}
