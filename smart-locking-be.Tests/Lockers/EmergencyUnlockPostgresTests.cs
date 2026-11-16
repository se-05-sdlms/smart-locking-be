using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class EmergencyUnlockPostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_emergency_test_{Guid.NewGuid():N}";
    private readonly Guid lockerId = Guid.NewGuid();
    private readonly Guid compartmentId = Guid.NewGuid();
    private readonly Guid otherCompartmentId = Guid.NewGuid();
    private readonly Guid userId = Guid.NewGuid();
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
        db.AddRange(new User { Id = userId, Email = "emergency@example.test", PasswordHash = "unused", Role = UserRole.Administrator, Status = UserStatus.Active },
            new Locker { Id = lockerId, Code = "EMERGENCY-TEST", DeviceIdentifier = "EMERGENCY-TEST", Address = "Test", RecoveryAddress = "Test", ConnectionStatus = LockerConnectionStatus.Online, OperationalStatus = LockerOperationalStatus.Operational },
            new LockerCompartment { Id = compartmentId, LockerId = lockerId, Code = "A1", HardwareCode = "A1", HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational },
            new LockerCompartment { Id = otherCompartmentId, LockerId = lockerId, Code = "A2", HardwareCode = "A2", HardwareChannel = 2, OperationalStatus = LockerCompartmentOperationalStatus.Operational });
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

    [EmergencyPostgresFact]
    public async Task OverlappingSameCompartment_RejectsDuplicateWithoutPublishingOrCreatingRows()
    {
        var dispatcher = new BarrierDispatcher();
        Task<EmergencyUnlockResponse> first = Open(dispatcher);
        await dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() => Open(dispatcher).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(LockerErrorCodes.UnlockInProgress, error.ErrorCode);
            Assert.Equal(1, dispatcher.Commands);
        }
        finally { dispatcher.Release.TrySetResult(); }
        Assert.Equal(EmergencyUnlockResult.Succeeded, (await first).Result);
        await using var db = Context();
        Assert.Single(await db.EmergencyUnlocks.ToListAsync());
        Assert.Single(await db.LockerAccessEvents.ToListAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync(item => item.Action == "Locker.UnlockCommand" || item.Action == "Locker.EmergencyUnlock"));
    }

    [EmergencyPostgresFact]
    public async Task SequentialEmergencyOpensAfterCompletion_AreAllowedWithoutCooldown()
    {
        var dispatcher = new BarrierDispatcher { PauseFirst = false };
        await Open(dispatcher);
        await Open(dispatcher);
        Assert.Equal(2, dispatcher.Commands);
        await using var db = Context();
        Assert.Equal(2, await db.EmergencyUnlocks.CountAsync());
        Assert.Equal(2, await db.LockerAccessEvents.CountAsync());
    }

    [EmergencyPostgresFact]
    public async Task DifferentCompartments_DoNotWaitForEachOthersDispatch()
    {
        var dispatcher = new BarrierDispatcher();
        Task<EmergencyUnlockResponse> first = Open(dispatcher);
        await dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var second = await Open(dispatcher, otherCompartmentId).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(EmergencyUnlockResult.Succeeded, second.Result);
            Assert.Equal(2, dispatcher.Commands);
        }
        finally { dispatcher.Release.TrySetResult(); }
        await first;
        await using var db = Context();
        Assert.Equal(2, await db.EmergencyUnlocks.CountAsync());
    }

    [EmergencyPostgresFact]
    public async Task FailedDispatch_IsRecordedAndReleasesCompartmentForNextRequest()
    {
        var dispatcher = new BarrierDispatcher { PauseFirst = false, FailFirst = true };
        Assert.Equal(EmergencyUnlockResult.Failed, (await Open(dispatcher)).Result);
        Assert.Equal(EmergencyUnlockResult.Succeeded, (await Open(dispatcher)).Result);
        Assert.Equal(2, dispatcher.Commands);
        await using var db = Context();
        Assert.Equal(1, await db.EmergencyUnlocks.CountAsync(item => item.Result == EmergencyUnlockResult.Failed));
        Assert.Equal(1, await db.EmergencyUnlocks.CountAsync(item => item.Result == EmergencyUnlockResult.Succeeded));
    }

    [EmergencyPostgresFact]
    public async Task PersistenceFailureAfterPublish_RollsBackRecordsAndReleasesCompartment()
    {
        var dispatcher = new BarrierDispatcher { PauseFirst = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Open(dispatcher, interceptor: new FailSecondSave()));
        Assert.Equal(1, dispatcher.Commands);
        await using (var db = Context())
        {
            Assert.Empty(await db.EmergencyUnlocks.ToListAsync());
            Assert.Empty(await db.LockerAccessEvents.ToListAsync());
            Assert.Empty(await db.AuditLogs.Where(item => item.Action == "Locker.UnlockCommand" || item.Action == "Locker.EmergencyUnlock").ToListAsync());
        }
        // A published command cannot be undone by SQL rollback. This is a new,
        // intentional request after the failed transaction, not an automatic retry.
        Assert.Equal(EmergencyUnlockResult.Succeeded, (await Open(dispatcher)).Result);
        Assert.Equal(2, dispatcher.Commands);
    }

    [EmergencyPostgresFact]
    public async Task InvalidScopeAndCompartment_DoNotPublishOrCreateIntent()
    {
        var dispatcher = new BarrierDispatcher { PauseFirst = false };
        await Assert.ThrowsAsync<ForbiddenException>(() => Open(dispatcher, role: nameof(UserRole.Resident)));
        await Assert.ThrowsAsync<NotFoundException>(() => Open(dispatcher, Guid.NewGuid()));
        Assert.Equal(0, dispatcher.Commands);
        await using var db = Context();
        Assert.Empty(await db.EmergencyUnlocks.ToListAsync());
        Assert.Empty(await db.LockerAccessEvents.ToListAsync());
    }

    private ApplicationDbContext Context(SaveChangesInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new ApplicationDbContext(options.Options);
    }

    private async Task<EmergencyUnlockResponse> Open(ILockerCommandDispatcher dispatcher, Guid? compartment = null, string role = nameof(UserRole.Administrator), SaveChangesInterceptor? interceptor = null)
    {
        await using var db = Context(interceptor);
        var access = new LockerAccessService(db, dispatcher, TimeProvider.System);
        var service = new EmergencyUnlockService(db, access, TimeProvider.System);
        return await service.CreateAsync(userId, role, new EmergencyUnlockRequest(lockerId, compartment ?? compartmentId, null, "Investigate emergency access"));
    }

    private sealed class FailSecondSave : SaveChangesInterceptor
    {
        private int saves;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++saves == 2) throw new InvalidOperationException("Test persistence failure after publish");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class BarrierDispatcher : ILockerCommandDispatcher
    {
        private int commands;
        public int Commands => Volatile.Read(ref commands);
        public bool PauseFirst { get; init; } = true;
        public bool FailFirst { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DispatchUnlockAsync(LockerUnlockCommand command, CancellationToken cancellationToken = default)
        {
            int number = Interlocked.Increment(ref commands);
            if (number != 1) return;
            Entered.TrySetResult();
            if (PauseFirst) await Release.Task.WaitAsync(cancellationToken);
            if (FailFirst) throw new IOException("Test broker unavailable");
        }
    }
}

internal sealed class EmergencyPostgresFactAttribute : FactAttribute
{
    public EmergencyPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMART_LOCKING_TEST_POSTGRES")))
            Skip = "Set SMART_LOCKING_TEST_POSTGRES to an account allowed to create disposable PostgreSQL test databases.";
    }
}
