using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Admin;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Admin;

public sealed class AdminUserServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static (AdminUserService Service, ApplicationDbContext DbContext) CreateTestService()
    {
        var dbContext = CreateInMemoryDbContext();
        var passwordHashService = new Pbkdf2PasswordHashService();
        var tokenHashService = new Sha256TokenHashService();
        var service = new AdminUserService(dbContext, passwordHashService, tokenHashService);
        return (service, dbContext);
    }

    [Fact]
    public async Task GetResidentsAsync_ReturnsOnlyResidentsWithPagingAndFilter()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var resident1 = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = "0900000001",
                Email = "resident1@boxora.com",
                Role = UserRole.Resident,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
            };
            var resident2 = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = "0900000002",
                Email = "resident2@boxora.com",
                Role = UserRole.Resident,
                Status = UserStatus.Locked,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            var operatorUser = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = "0900000003",
                Email = "operator@boxora.com",
                Role = UserRole.LockerOperator,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.Users.AddRange(resident1, resident2, operatorUser);
            await dbContext.SaveChangesAsync();

            // Act 1: Get all residents
            var resultAll = await service.GetResidentsAsync(new GetUsersFilterRequest());
            Assert.Equal(2, resultAll.TotalCount);
            Assert.Equal(2, resultAll.Items.Count);

            // Act 2: Filter by status Locked
            var resultLocked = await service.GetResidentsAsync(new GetUsersFilterRequest(Status: UserStatus.Locked));
            Assert.Equal(1, resultLocked.TotalCount);
            Assert.Equal(resident2.Id, resultLocked.Items[0].UserId);

            // Act 3: Filter by search keyword
            var resultSearch = await service.GetResidentsAsync(new GetUsersFilterRequest(Search: "resident1"));
            Assert.Equal(1, resultSearch.TotalCount);
            Assert.Equal(resident1.Id, resultSearch.Items[0].UserId);
        }
    }

    [Fact]
    public async Task GetResidentByIdAsync_WhenResidentExists_ReturnsDetailResponse()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                PhoneNumber = "0911223344",
                Email = "res@boxora.com",
                Role = UserRole.Resident,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var profile = new ResidentProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FullName = "Nguyen Cư Dân",
                DateOfBirth = new DateOnly(1990, 1, 1),
                DeliveryApprovalMode = DeliveryApprovalMode.Manual,
                PersonalQrTokenHash = "hash",
                PersonalQrIssuedAt = DateTimeOffset.UtcNow
            };
            dbContext.Users.Add(user);
            dbContext.ResidentProfiles.Add(profile);
            await dbContext.SaveChangesAsync();

            var detail = await service.GetResidentByIdAsync(userId);

            Assert.NotNull(detail);
            Assert.Equal(userId, detail.UserId);
            Assert.Equal("Nguyen Cư Dân", detail.FullName);
            Assert.Equal(new DateOnly(1990, 1, 1), detail.DateOfBirth);
            Assert.Equal(DeliveryApprovalMode.Manual, detail.DeliveryApprovalMode);
        }
    }

    [Fact]
    public async Task GetResidentByIdAsync_WhenNotResident_ThrowsKeyNotFoundException()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var opUser = new User
            {
                Id = Guid.NewGuid(),
                Email = "op@boxora.com",
                Role = UserRole.LockerOperator,
                Status = UserStatus.Active
            };
            dbContext.Users.Add(opUser);
            await dbContext.SaveChangesAsync();

            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetResidentByIdAsync(opUser.Id));
        }
    }

    [Fact]
    public async Task UpdateResidentStatusAsync_WhenLocked_UpdatesStatusCreatesAuditLogAndRevokesTokens()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Email = "res@boxora.com",
                Role = UserRole.Resident,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
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

            var request = new UpdateUserStatusRequest(UserStatus.Locked, "Vi phạm chính sách sử dụng");
            await service.UpdateResidentStatusAsync(adminId, userId, request, "127.0.0.1");

            var updatedUser = await dbContext.Users.FirstAsync(u => u.Id == userId);
            Assert.Equal(UserStatus.Locked, updatedUser.Status);

            var updatedToken = await dbContext.RefreshTokens.FirstAsync(t => t.Id == token.Id);
            Assert.NotNull(updatedToken.RevokedAt);
            Assert.Equal("127.0.0.1", updatedToken.RevokedByIp);

            var auditLog = await dbContext.AuditLogs.FirstOrDefaultAsync(l => l.EntityId == userId && l.Action == "LockResident");
            Assert.NotNull(auditLog);
            Assert.Equal(adminId, auditLog.ActorUserId);
            Assert.Equal(AuditLogResult.Succeeded, auditLog.Result);
            Assert.Contains("Vi phạm chính sách sử dụng", auditLog.Details);
        }
    }

    [Fact]
    public async Task GetOperatorsAsync_ReturnsOperatorsWithActiveAssignmentCounts()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var opId = Guid.NewGuid();
            var op = new User
            {
                Id = opId,
                Email = "operator1@boxora.com",
                Role = UserRole.LockerOperator,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var assignment1 = new OperatorAssignment
            {
                Id = Guid.NewGuid(),
                OperatorUserId = opId,
                BuildingId = Guid.NewGuid(),
                AssignedByUserId = adminId,
                AssignedAt = DateTimeOffset.UtcNow
            };
            var assignment2 = new OperatorAssignment
            {
                Id = Guid.NewGuid(),
                OperatorUserId = opId,
                LockerClusterId = Guid.NewGuid(),
                AssignedByUserId = adminId,
                AssignedAt = DateTimeOffset.UtcNow,
                RevokedAt = DateTimeOffset.UtcNow // Revoked
            };
            dbContext.Users.Add(op);
            dbContext.OperatorAssignments.AddRange(assignment1, assignment2);
            await dbContext.SaveChangesAsync();

            var result = await service.GetOperatorsAsync(new GetUsersFilterRequest());
            Assert.Equal(1, result.TotalCount);
            Assert.Equal(1, result.Items[0].ActiveAssignmentsCount);
        }
    }

    [Fact]
    public async Task CreateOperatorAsync_WhenValid_CreatesUserWithMustChangePasswordAndAuditLog()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var request = new CreateOperatorRequest(
                "Le Van Operator",
                "op.new@boxora.com",
                "0933445566",
                null, // Random temporary password
                null, null, null, null
            );

            var response = await service.CreateOperatorAsync(adminId, request, "10.0.0.1");

            Assert.NotNull(response);
            Assert.Equal("op.new@boxora.com", response.Email);
            Assert.Equal("Le Van Operator", response.FullName);
            Assert.NotEmpty(response.TemporaryPassword);
            Assert.Equal(UserStatus.Active, response.Status);

            var user = await dbContext.Users.FirstAsync(u => u.Id == response.UserId);
            Assert.Equal(UserRole.LockerOperator, user.Role);
            Assert.True(user.MustChangePassword);

            var auditLog = await dbContext.AuditLogs.FirstOrDefaultAsync(l => l.EntityId == response.UserId && l.Action == "CreateOperator");
            Assert.NotNull(auditLog);
            Assert.Equal(adminId, auditLog.ActorUserId);
        }
    }

    [Fact]
    public async Task CreateOperatorAsync_WhenEmailExists_ThrowsInvalidOperationException()
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

            var request = new CreateOperatorRequest("Name", "existing@boxora.com", null, null, null, null, null, null);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateOperatorAsync(adminId, request));
        }
    }

    [Fact]
    public async Task CreateOperatorAsync_WithMultipleScopes_ThrowsArgumentException()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var request = new CreateOperatorRequest(
                "Name",
                "scope.invalid@boxora.com",
                null,
                null,
                BuildingId: Guid.NewGuid(),
                LockerClusterId: Guid.NewGuid(), // Invalid: 2 scopes provided
                LockerId: null,
                AssignmentReason: null
            );

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.CreateOperatorAsync(adminId, request));
        }
    }

    [Fact]
    public async Task AssignOperatorScopeAsync_WithValidScope_CreatesAssignmentAndAuditLog()
    {
        var (service, dbContext) = CreateTestService();
        await using (dbContext)
        {
            var adminId = Guid.NewGuid();
            var opId = Guid.NewGuid();
            var buildingId = Guid.NewGuid();

            var op = new User
            {
                Id = opId,
                Email = "op.assign@boxora.com",
                Role = UserRole.LockerOperator,
                Status = UserStatus.Active
            };
            var building = new Building
            {
                Id = buildingId,
                Code = "B01",
                Name = "Tòa Landmark",
                Address = "Khu Công Nghệ Cao",
                Status = BuildingStatus.Active
            };
            dbContext.Users.Add(op);
            dbContext.Buildings.Add(building);
            await dbContext.SaveChangesAsync();

            var request = new AssignOperatorScopeRequest(BuildingId: buildingId, LockerClusterId: null, LockerId: null, Reason: "Phụ trách tòa Landmark");
            var result = await service.AssignOperatorScopeAsync(adminId, opId, request, "127.0.0.1");

            Assert.NotNull(result);
            Assert.Equal(buildingId, result.BuildingId);
            Assert.Equal("Tòa Landmark", result.BuildingName);

            var persisted = await dbContext.OperatorAssignments.FirstAsync(a => a.Id == result.Id);
            Assert.Equal(opId, persisted.OperatorUserId);
            Assert.Equal(adminId, persisted.AssignedByUserId);

            var auditLog = await dbContext.AuditLogs.FirstOrDefaultAsync(l => l.EntityId == result.Id && l.Action == "AssignOperatorScope");
            Assert.NotNull(auditLog);
            Assert.Equal(adminId, auditLog.ActorUserId);
        }
    }

    [Fact]
    public async Task RevokeOperatorScopeAsync_UpdatesRevokedAtAndCreatesAuditLog()
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
                Email = "op.revoke@boxora.com",
                Role = UserRole.LockerOperator,
                Status = UserStatus.Active
            };
            var assignment = new OperatorAssignment
            {
                Id = assignmentId,
                OperatorUserId = opId,
                BuildingId = Guid.NewGuid(),
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

            var auditLog = await dbContext.AuditLogs.FirstOrDefaultAsync(l => l.EntityId == assignmentId && l.Action == "RevokeOperatorScope");
            Assert.NotNull(auditLog);
            Assert.Equal(adminId, auditLog.ActorUserId);
        }
    }
}
