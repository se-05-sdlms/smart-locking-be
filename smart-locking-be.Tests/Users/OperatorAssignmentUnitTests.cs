using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Users;

public sealed class OperatorAssignmentUnitTests
{
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _operatorId = Guid.NewGuid();
    private readonly Guid _lockerId = Guid.NewGuid();

    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static UserService CreateService(ApplicationDbContext db) => new(db, null!, null!);

    private async Task SeedBaseDataAsync(ApplicationDbContext db, UserStatus operatorStatus = UserStatus.Active)
    {
        db.Users.AddRange(
            new User
            {
                Id = _adminId,
                Email = "admin@example.test",
                Role = UserRole.Administrator,
                Status = UserStatus.Active
            },
            new User
            {
                Id = _operatorId,
                Email = "operator@example.test",
                Role = UserRole.LockerOperator,
                Status = operatorStatus
            }
        );

        db.Lockers.Add(new Locker
        {
            Id = _lockerId,
            Code = "LOCKER-001",
            DeviceIdentifier = "dev-001",
            OperationalStatus = LockerOperationalStatus.Operational
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AssignOperatorScope_ActiveOperatorAndValidLocker_SucceedsAndCreatesAssignmentAndAuditLog()
    {
        // Arrange
        await using var db = CreateDbContext();
        await SeedBaseDataAsync(db, UserStatus.Active);
        var service = CreateService(db);
        var request = new AssignOperatorScopeRequest(_lockerId, "Assigned for zone A");

        // Act
        var response = await service.AssignOperatorScopeAsync(_adminId, _operatorId, request, "127.0.0.1");

        // Assert
        Assert.NotNull(response);
        Assert.Equal(_lockerId, response.LockerId);
        Assert.Equal("LOCKER-001", response.LockerCode);
        Assert.Equal("Assigned for zone A", response.Reason);
        Assert.Null(response.RevokedAt);

        // Verify database persistence
        var assignment = await db.OperatorAssignments.SingleOrDefaultAsync(a => a.Id == response.Id);
        Assert.NotNull(assignment);
        Assert.Equal(_operatorId, assignment.OperatorUserId);
        Assert.Equal(_lockerId, assignment.LockerId);
        Assert.Equal(_adminId, assignment.AssignedByUserId);
        Assert.Null(assignment.RevokedAt);

        var auditLog = await db.AuditLogs.SingleOrDefaultAsync(a =>
            a.Action == "OperatorAssignment.Created" && a.EntityId == response.Id);
        Assert.NotNull(auditLog);
        Assert.Equal(_adminId, auditLog.ActorUserId);
        Assert.Equal("OperatorAssignment", auditLog.EntityType);
        Assert.Equal(AuditLogResult.Succeeded, auditLog.Result);
        Assert.Equal("127.0.0.1", auditLog.IpAddress);
    }

    [Fact]
    public async Task AssignOperatorScope_DisabledOperator_ThrowsConflictException()
    {
        // Arrange
        await using var db = CreateDbContext();
        await SeedBaseDataAsync(db, UserStatus.Disabled);
        var service = CreateService(db);
        var request = new AssignOperatorScopeRequest(_lockerId, "Test disabled operator");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignOperatorScopeAsync(_adminId, _operatorId, request));

        Assert.Equal(UserErrorCodes.Inactive, ex.ErrorCode);
        Assert.Empty(await db.OperatorAssignments.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "OperatorAssignment.Created").ToListAsync());
    }

    [Fact]
    public async Task AssignOperatorScope_LockedOperator_ThrowsConflictException()
    {
        // Arrange
        await using var db = CreateDbContext();
        await SeedBaseDataAsync(db, UserStatus.Locked);
        var service = CreateService(db);
        var request = new AssignOperatorScopeRequest(_lockerId, "Test locked operator");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignOperatorScopeAsync(_adminId, _operatorId, request));

        Assert.Equal(UserErrorCodes.Inactive, ex.ErrorCode);
        Assert.Empty(await db.OperatorAssignments.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "OperatorAssignment.Created").ToListAsync());
    }

    [Fact]
    public async Task AssignOperatorScope_LockerNotFound_ThrowsNotFoundException()
    {
        // Arrange
        await using var db = CreateDbContext();
        await SeedBaseDataAsync(db, UserStatus.Active);
        var service = CreateService(db);
        var nonExistentLockerId = Guid.NewGuid();
        var request = new AssignOperatorScopeRequest(nonExistentLockerId, "Missing locker");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AssignOperatorScopeAsync(_adminId, _operatorId, request));

        Assert.Equal(LockerErrorCodes.NotFound, ex.ErrorCode);
        Assert.Empty(await db.OperatorAssignments.ToListAsync());
    }

    [Fact]
    public async Task AssignOperatorScope_LockerAlreadyAssignedActive_ThrowsConflictException()
    {
        // Arrange
        await using var db = CreateDbContext();
        await SeedBaseDataAsync(db, UserStatus.Active);

        // Pre-existing active assignment on this locker
        db.OperatorAssignments.Add(new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = _operatorId,
            LockerId = _lockerId,
            AssignedByUserId = _adminId,
            AssignedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedAt = null
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var request = new AssignOperatorScopeRequest(_lockerId, "Re-assigning same locker");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignOperatorScopeAsync(_adminId, _operatorId, request));

        Assert.Equal(UserErrorCodes.AssignmentConflict, ex.ErrorCode);
        // Ensure only the original assignment exists
        Assert.Single(await db.OperatorAssignments.ToListAsync());
    }

    [Fact]
    public async Task AssignOperatorScope_LockerPreviouslyRevoked_CanBeReassignedSuccessfully()
    {
        // Arrange
        await using var db = CreateDbContext();
        await SeedBaseDataAsync(db, UserStatus.Active);

        // Pre-existing revoked assignment
        db.OperatorAssignments.Add(new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = Guid.NewGuid(),
            LockerId = _lockerId,
            AssignedByUserId = _adminId,
            AssignedAt = DateTimeOffset.UtcNow.AddDays(-5),
            RevokedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var request = new AssignOperatorScopeRequest(_lockerId, "New assignment after revoke");

        // Act
        var response = await service.AssignOperatorScopeAsync(_adminId, _operatorId, request);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(_lockerId, response.LockerId);
        var assignments = await db.OperatorAssignments.Where(a => a.LockerId == _lockerId).ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.Single(assignments, a => a.RevokedAt == null);
    }

    [Fact]
    public async Task AssignOperatorScope_OperatorNotFound_ThrowsNotFoundException()
    {
        // Arrange
        await using var db = CreateDbContext();
        await SeedBaseDataAsync(db, UserStatus.Active);
        var service = CreateService(db);
        var nonExistentOperatorId = Guid.NewGuid();
        var request = new AssignOperatorScopeRequest(_lockerId, "Operator not found");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AssignOperatorScopeAsync(_adminId, nonExistentOperatorId, request));

        Assert.Equal(UserErrorCodes.NotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task AssignOperatorScope_UserRoleNotOperator_ThrowsNotFoundException()
    {
        // Arrange
        await using var db = CreateDbContext();
        var residentId = Guid.NewGuid();
        db.Users.AddRange(
            new User { Id = _adminId, Role = UserRole.Administrator, Status = UserStatus.Active },
            new User { Id = residentId, Role = UserRole.Resident, Status = UserStatus.Active }
        );
        db.Lockers.Add(new Locker { Id = _lockerId, Code = "L-01", DeviceIdentifier = "dev-1" });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var request = new AssignOperatorScopeRequest(_lockerId, "Assigning to resident");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AssignOperatorScopeAsync(_adminId, residentId, request));

        Assert.Equal(UserErrorCodes.NotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task AssignOperatorScope_EmptyLockerId_ThrowsBadRequestException()
    {
        // Arrange
        await using var db = CreateDbContext();
        await SeedBaseDataAsync(db, UserStatus.Active);
        var service = CreateService(db);
        var request = new AssignOperatorScopeRequest(Guid.Empty, "Empty locker id");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.AssignOperatorScopeAsync(_adminId, _operatorId, request));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("lockerId"));
    }
}
