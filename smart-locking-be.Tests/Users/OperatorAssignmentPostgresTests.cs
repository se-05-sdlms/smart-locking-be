using Microsoft.EntityFrameworkCore;
using Npgsql;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using smart_locking_be.Tests.Auth;

namespace smart_locking_be.Tests.Users;

public sealed class OperatorAssignmentPostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_assignment_test_{Guid.NewGuid():N}";
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
    public async Task DisabledOperator_RejectsAssignmentWithoutAuditOrReservation()
        => await RejectInactive(UserStatus.Disabled);

    [PostgresFact]
    public async Task LockedOperator_RejectsAssignmentWithoutAuditOrReservation()
        => await RejectInactive(UserStatus.Locked);

    private async Task RejectInactive(UserStatus status)
    {
        await using (var seed = Context())
            await seed.Users.Where(u => u.Id == operatorId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, status));
        await using (var db = Context())
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() => Service(db).AssignOperatorScopeAsync(adminId, operatorId, new(lockerId, "Assign")));
            Assert.Equal(UserErrorCodes.Inactive, error.ErrorCode);
        }
        await AssertNoAssignment();
    }

    [PostgresFact]
    public async Task MissingLocker_ReturnsNotFoundInsteadOfForeignKeyFailure()
    {
        await using (var db = Context())
        {
            var error = await Assert.ThrowsAsync<NotFoundException>(() => Service(db).AssignOperatorScopeAsync(adminId, operatorId, new(Guid.NewGuid(), "Assign")));
            Assert.Equal(LockerErrorCodes.NotFound, error.ErrorCode);
        }
        await AssertNoAssignment();
    }

    [PostgresFact]
    public async Task ActiveOperatorAndExistingLocker_PersistsAssignmentAndAudit()
    {
        await using (var db = Context())
        {
            var result = await Service(db).AssignOperatorScopeAsync(adminId, operatorId, new(lockerId, "Assign"));
            Assert.Equal(lockerId, result.LockerId);
            Assert.Equal("ASSIGNMENT", result.LockerCode);
        }
        await using var read = Context();
        Assert.Equal(operatorId, (await read.OperatorAssignments.SingleAsync()).OperatorUserId);
        Assert.Single(await read.AuditLogs.Where(a => a.Action == "OperatorAssignment.Created").ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentAssignments_OnlyOneSucceedsAndTheOtherReturnsConflict()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Assign()
        {
            await using var db = Context();
            await start.Task;
            return await Record.ExceptionAsync(() => Service(db).AssignOperatorScopeAsync(adminId, operatorId, new(lockerId, "Assign")));
        }
        Task<Exception?> first = Assign(), second = Assign();
        start.SetResult();
        var errors = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(errors, e => e is null);
        Assert.Single(errors, e => e is ConflictException { ErrorCode: UserErrorCodes.AssignmentConflict });
        await using var read = Context();
        Assert.Single(await read.OperatorAssignments.ToListAsync());
        Assert.Single(await read.AuditLogs.Where(a => a.Action == "OperatorAssignment.Created").ToListAsync());
    }

    private async Task AssertNoAssignment()
    {
        await using var read = Context();
        Assert.Empty(await read.OperatorAssignments.ToListAsync());
        Assert.Empty(await read.AuditLogs.Where(a => a.Action == "OperatorAssignment.Created").ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentStatusChange_CommitsBeforeAssignmentValidation_AndInactiveOperatorIsRejected()
    {
        await using var status = Context();
        await using var transaction = await status.Database.BeginTransactionAsync();
        await status.Users.Where(u => u.Id == operatorId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Disabled));
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Exception?> assignment = Assign();
        try
        {
            int pid = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var observer = new NpgsqlConnection(connectionString);
            await observer.OpenAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!assignment.IsCompleted)
            {
                await using var command = new NpgsqlCommand("SELECT wait_event_type = 'Lock' FROM pg_stat_activity WHERE pid = @pid", observer);
                command.Parameters.AddWithValue("pid", pid);
                if (await command.ExecuteScalarAsync(timeout.Token) is true) break;
                await Task.Delay(20, timeout.Token);
            }
        }
        finally { await transaction.CommitAsync(); }
        Assert.IsType<ConflictException>(await assignment.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(UserErrorCodes.Inactive, ((ConflictException)(await assignment)!).ErrorCode);
        await AssertNoAssignment();

        async Task<Exception?> Assign()
        {
            await using var db = Context();
            await db.Database.OpenConnectionAsync();
            started.SetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            return await Record.ExceptionAsync(() => Service(db).AssignOperatorScopeAsync(adminId, operatorId, new(lockerId, "Assign")));
        }
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
    private static UserService Service(ApplicationDbContext db) => new(db, null!, null!);
}
