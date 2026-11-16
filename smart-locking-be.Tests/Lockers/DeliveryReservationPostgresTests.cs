using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class DeliveryReservationPostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_reservation_test_{Guid.NewGuid():N}";
    private readonly Guid lockerId = Guid.NewGuid();
    private readonly Guid policyId = Guid.NewGuid();
    private readonly Guid requestId = Guid.NewGuid();
    private readonly Guid compartmentId = Guid.NewGuid();
    private readonly Guid otherCompartmentId = Guid.NewGuid();
    private readonly Sha256TokenHashService tokens = new();
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
        var user = new User { Id = Guid.NewGuid(), Email = "reservation@example.test", PasswordHash = "unused", Role = UserRole.Administrator, Status = UserStatus.Active };
        db.AddRange(user,
            new SystemPolicy { Id = policyId, Version = 1, CreatedByUser = user, Currency = "VND", CompartmentReservationMinutes = 5, GuestSessionTimeoutMinutes = 10, ManualApprovalTimeoutMinutes = 10, OtpMaxAttempts = 5, OtpLockoutMinutes = 15, MaxStorageHours = 24 },
            new Locker { Id = lockerId, Code = "RESERVATION-TEST", DeviceIdentifier = "RESERVATION-TEST", Address = "Test", RecoveryAddress = "Test", ConnectionStatus = LockerConnectionStatus.Online, OperationalStatus = LockerOperationalStatus.Operational },
            new LockerCompartment { Id = compartmentId, LockerId = lockerId, Code = "A1", HardwareCode = "A1", HardwareChannel = 1, DoorStatus = DoorStatus.Closed, OperationalStatus = LockerCompartmentOperationalStatus.Operational },
            new LockerCompartment { Id = otherCompartmentId, LockerId = lockerId, Code = "A2", HardwareCode = "A2", HardwareChannel = 2, DoorStatus = DoorStatus.Closed, OperationalStatus = LockerCompartmentOperationalStatus.Operational },
            Request(requestId));
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
    public async Task OfflineGuard_ReleasesDurablyAndNextRequestCanReuseCompartment()
    {
        Guid requestForReuse;
        await using (var db = Context())
        {
            (await db.Lockers.SingleAsync()).ConnectionStatus = LockerConnectionStatus.Offline;
            await db.SaveChangesAsync();
        }
        var dispatcher = new Dispatcher();
        var error = await Assert.ThrowsAsync<ConflictException>(() => Open(dispatcher));
        Assert.Equal(LockerErrorCodes.Offline, error.ErrorCode);
        Assert.Equal(0, dispatcher.Commands);
        await using (var db = Context())
        {
            var reservation = await db.CompartmentReservations.SingleAsync();
            Assert.NotNull(reservation.ReleasedAt);
            Assert.Equal(compartmentId, reservation.LockerCompartmentId);
            Assert.Equal(DeliveryRequestStatus.Failed, (await db.DeliveryRequests.SingleAsync()).Status);
            Assert.Equal(DeliveryRequestFailureCode.DeviceUnavailable, (await db.DeliveryRequests.SingleAsync()).FailureCode);
            Assert.Equal(AuditLogResult.Failed, (await db.AuditLogs.SingleAsync(item => item.Action == "DeliveryRequest.CompartmentOpenFailed")).Result);
            Assert.Empty(await db.LockerAccessEvents.ToListAsync());
            (await db.Lockers.SingleAsync()).ConnectionStatus = LockerConnectionStatus.Online;
            var next = Request(Guid.NewGuid());
            db.Add(next);
            await db.SaveChangesAsync();
            requestForReuse = next.Id;
        }
        var result = await Open(dispatcher, requestForReuse);
        Assert.Equal(compartmentId, result.CompartmentId);
        Assert.Equal(1, dispatcher.Commands);
    }

    [EmergencyPostgresFact]
    public async Task SameRequestInFlight_CannotAllocateOrPublishAgain()
    {
        var dispatcher = new Dispatcher { Pause = true };
        Task<CompartmentReservationResponse> first = Open(dispatcher);
        await dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() => Open(dispatcher).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(DeliveryErrorCodes.InvalidState, error.ErrorCode);
            Assert.Equal(1, dispatcher.Commands);
        }
        finally { dispatcher.Release.TrySetResult(); }
        await first;
        await using var db = Context();
        Assert.Null((await db.CompartmentReservations.SingleAsync()).ReleasedAt);
        Assert.Single(await db.LockerAccessEvents.ToListAsync());
    }

    [EmergencyPostgresFact]
    public async Task OfflineFailure_ReleasesOnlyTheReservationCreatedByThisOperation()
    {
        Guid unrelatedRequest = Guid.NewGuid();
        Guid unrelatedReservation = Guid.NewGuid();
        await using (var db = Context())
        {
            db.Add(Request(unrelatedRequest));
            db.Add(new CompartmentReservation { Id = unrelatedReservation, LockerCompartmentId = otherCompartmentId, DeliveryRequestId = unrelatedRequest, ReservedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
            (await db.Lockers.SingleAsync()).ConnectionStatus = LockerConnectionStatus.Offline;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<ConflictException>(() => Open(new Dispatcher()));
        await using var read = Context();
        Assert.Null((await read.CompartmentReservations.SingleAsync(item => item.Id == unrelatedReservation)).ReleasedAt);
        Assert.NotNull((await read.CompartmentReservations.SingleAsync(item => item.DeliveryRequestId == requestId)).ReleasedAt);
    }

    [EmergencyPostgresFact]
    public async Task OfflineFailure_DoesNotReleaseAnotherReservationWithSameDeliveryOwner()
    {
        Guid previousReservation = Guid.NewGuid();
        await using (var db = Context())
        {
            db.Add(new CompartmentReservation { Id = previousReservation, LockerCompartmentId = otherCompartmentId, DeliveryRequestId = requestId, ReservedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
            (await db.Lockers.SingleAsync()).ConnectionStatus = LockerConnectionStatus.Offline;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<ConflictException>(() => Open(new Dispatcher()));
        await using var read = Context();
        Assert.Null((await read.CompartmentReservations.SingleAsync(item => item.Id == previousReservation)).ReleasedAt);
        Assert.NotNull((await read.CompartmentReservations.SingleAsync(item => item.Id != previousReservation)).ReleasedAt);
    }

    [EmergencyPostgresFact]
    public async Task FailedDispatchResponse_ReleasesExactReservationAndPersistsFailure()
    {
        var dispatcher = new Dispatcher { Fail = true };
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Open(dispatcher));
        Assert.Equal(LockerErrorCodes.OpenFailed, error.ErrorCode);
        Assert.Equal(1, dispatcher.Commands);
        await using var db = Context();
        Assert.NotNull((await db.CompartmentReservations.SingleAsync()).ReleasedAt);
        Assert.Equal(DeliveryRequestStatus.Failed, (await db.DeliveryRequests.SingleAsync()).Status);
        Assert.Equal(LockerAccessResult.Failed, (await db.LockerAccessEvents.SingleAsync()).Result);
    }

    [EmergencyPostgresFact]
    public async Task PostPublishPersistenceFailure_KeepsDurableReservationForPhysicalAmbiguity()
    {
        var dispatcher = new Dispatcher();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Open(dispatcher, interceptor: new FailAccessSave()));
        Assert.Equal(1, dispatcher.Commands);
        await using var db = Context();
        Assert.Null((await db.CompartmentReservations.SingleAsync()).ReleasedAt);
        Assert.Equal(DeliveryRequestStatus.Allocated, (await db.DeliveryRequests.SingleAsync()).Status);
    }

    [EmergencyPostgresFact]
    public async Task InvalidSession_DoesNotReserveOrDispatch()
    {
        var dispatcher = new Dispatcher();
        await Assert.ThrowsAsync<UnauthorizedException>(() => Open(dispatcher, token: "wrong"));
        Assert.Equal(0, dispatcher.Commands);
        await using var db = Context();
        Assert.Empty(await db.CompartmentReservations.ToListAsync());
        Assert.Equal(DeliveryRequestStatus.Approved, (await db.DeliveryRequests.SingleAsync()).Status);
    }

    [EmergencyPostgresFact]
    public async Task NoAvailableCompartment_CommitsFailureWithoutReleasingExistingReservations()
    {
        await using (var db = Context())
        {
            foreach (Guid compartment in new[] { compartmentId, otherCompartmentId })
                db.Add(new CompartmentReservation { Id = Guid.NewGuid(), LockerCompartmentId = compartment, DeliveryRequestId = requestId, ReservedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
            await db.SaveChangesAsync();
        }
        var error = await Assert.ThrowsAsync<ConflictException>(() => Open(new Dispatcher()));
        Assert.Equal(LockerErrorCodes.NoAvailableCompartment, error.ErrorCode);
        await using var read = Context();
        Assert.Equal(DeliveryRequestFailureCode.NoCompartment, (await read.DeliveryRequests.SingleAsync()).FailureCode);
        Assert.All(await read.CompartmentReservations.ToListAsync(), item => Assert.Null(item.ReleasedAt));
    }

    private DeliveryRequest Request(Guid id) => new()
    {
        Id = id,
        LockerId = lockerId,
        SystemPolicyId = policyId,
        GuestSessionTokenHash = tokens.HashToken($"guest-{id}"),
        Status = DeliveryRequestStatus.Approved,
        SessionExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
    };

    private ApplicationDbContext Context(SaveChangesInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new ApplicationDbContext(options.Options);
    }

    private async Task<CompartmentReservationResponse> Open(Dispatcher dispatcher, Guid? request = null, string? token = null, SaveChangesInterceptor? interceptor = null)
    {
        await using var db = Context(interceptor);
        var service = new DeliveryRequestService(db, tokens, null!, new LockerAccessService(db, dispatcher, TimeProvider.System),
            new CompartmentAllocationService(db, TimeProvider.System), null!, null!, null!, TimeProvider.System);
        return await service.OpenCompartmentAsync(request ?? requestId, token ?? $"guest-{request ?? requestId}");
    }

    private sealed class FailAccessSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<LockerAccessEvent>().Any(item => item.State == EntityState.Added))
                throw new InvalidOperationException("Test database failure after OPEN publish");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Dispatcher : ILockerCommandDispatcher
    {
        private int commands;
        public int Commands => Volatile.Read(ref commands);
        public bool Pause { get; init; }
        public bool Fail { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task DispatchUnlockAsync(LockerUnlockCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref commands);
            Entered.TrySetResult();
            if (Pause) await Release.Task.WaitAsync(cancellationToken);
            if (Fail) throw new IOException("Test MQTT dispatch failure");
        }
    }
}
