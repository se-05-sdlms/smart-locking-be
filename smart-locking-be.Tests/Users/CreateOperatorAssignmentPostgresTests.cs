using Microsoft.EntityFrameworkCore;
using Npgsql;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using smart_locking_be.Tests.Auth;

namespace smart_locking_be.Tests.Users;

public sealed class CreateOperatorAssignmentPostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_create_assignment_test_{Guid.NewGuid():N}";
    private readonly Guid adminId = Guid.NewGuid();
    private readonly Guid operatorId = Guid.NewGuid();
    private readonly Guid lockerId = Guid.NewGuid();
    private string connectionString = string.Empty;
    private string adminConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        string? configured = Environment.GetEnvironmentVariable("SMART_LOCKING_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var builder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        adminConnectionString = builder.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await create.ExecuteNonQueryAsync();
        builder.Database = databaseName;
        connectionString = builder.ConnectionString;
        await using var db = Context();
        await db.Database.MigrateAsync();
        db.AddRange(
            new User { Id = adminId, Email = "admin@example.test", Role = UserRole.Administrator, Status = UserStatus.Active },
            new User { Id = operatorId, Email = "operator@example.test", Role = UserRole.LockerOperator, Status = UserStatus.Active },
            new Locker { Id = lockerId, Code = "ASSIGNMENT", DeviceIdentifier = "assignment-device", OperationalStatus = LockerOperationalStatus.Operational });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(adminConnectionString)) return;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task OccupiedLocker_ReturnsConflictWithoutCreatingAccountOrSendingEmail()
    {
        await using (var seed = Context())
        {
            seed.OperatorAssignments.Add(new() { Id = Guid.NewGuid(), OperatorUserId = operatorId, LockerId = lockerId, AssignedByUserId = adminId, AssignedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync();
        }
        var email = new RecordingEmail();
        await using (var db = Context())
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() => Service(db, email).CreateUserAsync(adminId, Request("new@example.test", lockerId)));
            Assert.Equal(UserErrorCodes.AssignmentConflict, error.ErrorCode);
        }
        await AssertNoNewAccount();
        Assert.Equal(0, email.Count);
    }

    [PostgresFact]
    public async Task UnknownLocker_ReturnsNotFoundWithoutCreatingAccount()
    {
        await using (var db = Context())
        {
            var error = await Assert.ThrowsAsync<NotFoundException>(() => Service(db).CreateUserAsync(adminId, Request("new@example.test", Guid.NewGuid())));
            Assert.Equal(LockerErrorCodes.NotFound, error.ErrorCode);
        }
        await AssertNoNewAccount();
    }

    [PostgresFact]
    public async Task EmptyLocker_ReturnsBadRequestWithoutCreatingAccount()
    {
        await using (var db = Context())
            await Assert.ThrowsAsync<BadRequestException>(() => Service(db).CreateUserAsync(adminId, Request("new@example.test", Guid.Empty)));
        await AssertNoNewAccount();
    }

    [PostgresFact]
    public async Task AvailableLocker_PersistsAccountProfileAssignmentAuditAndSendsEmail()
    {
        var email = new RecordingEmail();
        Guid id;
        await using (var db = Context())
            id = (await Service(db, email).CreateUserAsync(adminId, Request("new@example.test", lockerId))).Id;
        await using var read = Context();
        Assert.Equal(UserStatus.Active, (await read.Users.SingleAsync(u => u.Id == id)).Status);
        Assert.Equal("New operator", (await read.ResidentProfiles.SingleAsync(p => p.UserId == id)).FullName);
        Assert.Equal(id, (await read.OperatorAssignments.SingleAsync()).OperatorUserId);
        Assert.Single(await read.AuditLogs.Where(a => a.Action == "User.Created" && a.EntityId == id).ToListAsync());
        Assert.Equal(1, email.Count);
    }

    [PostgresFact]
    public async Task NoInitialLocker_CreatesUnassignedOperator()
    {
        await using (var db = Context())
            await Service(db).CreateUserAsync(adminId, Request("new@example.test", null));
        await using var read = Context();
        Assert.Equal(3, await read.Users.CountAsync());
        Assert.Empty(await read.OperatorAssignments.ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentCreates_OneAccountWinsAndOtherReturnsConflict()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var email = new RecordingEmail();
        async Task<Exception?> Create(string address)
        {
            await using var db = Context();
            await start.Task;
            return await Record.ExceptionAsync(() => Service(db, email).CreateUserAsync(adminId, Request(address, lockerId)));
        }
        var first = Create("first@example.test");
        var second = Create("second@example.test");
        start.SetResult();
        var errors = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(errors, e => e is null);
        Assert.Single(errors, e => e is ConflictException { ErrorCode: UserErrorCodes.AssignmentConflict });
        await using var read = Context();
        Assert.Equal(3, await read.Users.CountAsync());
        Assert.Single(await read.ResidentProfiles.ToListAsync());
        Assert.Single(await read.OperatorAssignments.ToListAsync());
        Assert.Single(await read.AuditLogs.Where(a => a.Action == "User.Created").ToListAsync());
        Assert.Equal(1, email.Count);
    }

    [PostgresFact]
    public async Task ConcurrentCreateAndAssign_OneWinsWithoutOrphanAccount()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Run(bool create)
        {
            await using var db = Context();
            await start.Task;
            return await Record.ExceptionAsync(async () =>
            {
                if (create) await Service(db).CreateUserAsync(adminId, Request("new@example.test", lockerId));
                else await Service(db).AssignOperatorScopeAsync(adminId, operatorId, new(lockerId, "Assign"));
            });
        }
        var create = Run(true);
        var assign = Run(false);
        start.SetResult();
        var errors = await Task.WhenAll(create, assign).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(errors, e => e is null);
        Assert.Single(errors, e => e is ConflictException { ErrorCode: UserErrorCodes.AssignmentConflict });
        await using var read = Context();
        Assert.Single(await read.OperatorAssignments.ToListAsync());
        Assert.Equal(errors[0] is null ? 3 : 2, await read.Users.CountAsync());
        Assert.Equal(errors[0] is null ? 1 : 0, await read.ResidentProfiles.CountAsync());
        Assert.Equal(errors[0] is null ? 1 : 0, await read.AuditLogs.CountAsync(a => a.Action == "User.Created"));
    }

    [PostgresFact]
    public async Task EmailUnavailable_AccountAndAssignmentRemainCommittedForReissue()
    {
        await using (var db = Context())
        {
            var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Service(db, new RecordingEmail { Fail = true }).CreateUserAsync(adminId, Request("new@example.test", lockerId)));
            Assert.Equal(UserErrorCodes.EmailUnavailable, error.ErrorCode);
        }
        await using var read = Context();
        var user = await read.Users.SingleAsync(u => u.Email == "new@example.test");
        Assert.Equal(user.Id, (await read.OperatorAssignments.SingleAsync()).OperatorUserId);
        Assert.Single(await read.ResidentProfiles.Where(p => p.UserId == user.Id).ToListAsync());
        Assert.Single(await read.AuditLogs.Where(a => a.Action == "User.Created" && a.EntityId == user.Id).ToListAsync());
    }

    private async Task AssertNoNewAccount()
    {
        await using var read = Context();
        Assert.Equal(2, await read.Users.CountAsync());
        Assert.Empty(await read.ResidentProfiles.ToListAsync());
        Assert.Empty(await read.AuditLogs.Where(a => a.Action == "User.Created").ToListAsync());
    }

    private static CreateUserRequest Request(string email, Guid? locker) => new("New operator", email, null, UserRole.LockerOperator, null, locker, "Initial");
    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
    private static UserService Service(ApplicationDbContext db, RecordingEmail? email = null) => new(db, new TestHash(), email ?? new RecordingEmail());
    private sealed class TestHash : IPasswordHashService
    {
        public string HashPassword(string password) => "test-hash";
        public bool VerifyPassword(string password, string passwordHash) => false;
    }
    private sealed class RecordingEmail : IEmailSender
    {
        private int count;
        public int Count => count;
        public bool Fail { get; init; }
        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
        {
            if (Fail) throw new ServiceUnavailableException("email/unavailable", "Test sender unavailable");
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        }
    }
}
