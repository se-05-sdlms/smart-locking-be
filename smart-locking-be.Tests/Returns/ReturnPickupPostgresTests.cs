using Microsoft.EntityFrameworkCore;
using Npgsql;
using smart_locking_be.Application.DTOs.Returns;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Returns;

// Each test migrates and drops its own random database. No application database is modified.
public sealed class ReturnPickupPostgresTests : IAsyncLifetime
{
    private const string Ip = "192.0.2.1";
    private readonly string databaseName = $"smart_locking_pickup_test_{Guid.NewGuid():N}";
    private readonly Guid lockerId = Guid.NewGuid();
    private readonly MutableClock clock = new();
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
        var locker = new Locker { Id = lockerId, Code = "PICKUP-A", DeviceIdentifier = "A", Address = "Test", RecoveryAddress = "Test", OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online };
        var user = new User { Id = Guid.NewGuid(), Email = "pickup@example.test", PasswordHash = "unused", Role = UserRole.Resident, Status = UserStatus.Active };
        var resident = new ResidentProfile { Id = Guid.NewGuid(), User = user, FullName = "Test resident" };
        var compartment = new LockerCompartment { Id = Guid.NewGuid(), Locker = locker, Code = "A1", HardwareCode = "A1", HardwareChannel = 1 };
        db.AddRange(locker, resident, compartment,
            new Locker { Id = Guid.NewGuid(), Code = "PICKUP-B", DeviceIdentifier = "B", Address = "Test", RecoveryAddress = "Test" },
            new ReturnRequest { Id = Guid.NewGuid(), Locker = locker, ResidentProfile = resident, AllocatedCompartment = compartment, ReturnCode = "123456", ReturnImageUrl = "https://example.test/proof.jpg", Status = ReturnRequestStatus.Deposited },
            new SystemPolicy { Id = Guid.NewGuid(), Version = 1, CreatedByUser = user, IsActive = true, EffectiveFrom = clock.GetUtcNow().AddDays(-1), Currency = "VND", GuestSessionTimeoutMinutes = 10, ManualApprovalTimeoutMinutes = 10, CompartmentReservationMinutes = 10, OtpMaxAttempts = 5, OtpLockoutMinutes = 15, MaxStorageHours = 24 });
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

    [PickupPostgresFact]
    public async Task FailedAttemptsPersistAcrossRequests_AndBlockCorrectCodeAtFive()
    {
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            await Assert.ThrowsAsync<NotFoundException>(() => Verify());
            await using var read = Context();
            Assert.Equal(attempt, (await read.Set<ReturnPickupAttempt>().SingleAsync()).FailedAttempts);
        }
        var error = await Assert.ThrowsAsync<TooManyRequestsException>(() => Verify());
        Assert.Equal(ReturnErrorCodes.PickupLocked, error.ErrorCode);
        Assert.Equal(900, error.RetryAfterSeconds);
        await Assert.ThrowsAsync<TooManyRequestsException>(() => Verify("123456"));
        await using var db = Context();
        Assert.Null((await db.ReturnRequests.SingleAsync()).ShipperSessionTokenHash);
        Assert.Equal(5, (await db.Set<ReturnPickupAttempt>().SingleAsync()).FailedAttempts);
    }

    [PickupPostgresFact]
    public async Task ConcurrentFailuresCannotLoseIncrementsOrExceedLimit()
    {
        Exception?[] errors = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Record.ExceptionAsync(() => Verify())));
        Assert.Equal(4, errors.Count(error => error is NotFoundException));
        Assert.Equal(16, errors.Count(error => error is TooManyRequestsException));
        await using var db = Context();
        Assert.Equal(5, (await db.Set<ReturnPickupAttempt>().SingleAsync()).FailedAttempts);
    }

    [PickupPostgresFact]
    public async Task ConcurrentExpiredBucketsCleanUpWithoutCrossBucketDeadlocks()
    {
        string[] lockers = ["PICKUP-A", "PICKUP-B"];
        string[] addresses = [Ip, "192.0.2.2"];
        foreach (string locker in lockers)
            foreach (string ip in addresses)
                await Assert.ThrowsAsync<NotFoundException>(() => Verify(locker: locker, ip: ip));
        clock.Now = clock.Now.AddMinutes(15);
        Exception?[] errors = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(index => Record.ExceptionAsync(() => Verify(locker: lockers[index % 2], ip: addresses[index / 2 % 2]))));
        Assert.Equal(16, errors.Count(error => error is NotFoundException));
        Assert.Equal(4, errors.Count(error => error is TooManyRequestsException));
        await using var db = Context();
        Assert.All(await db.Set<ReturnPickupAttempt>().ToListAsync(), bucket => Assert.Equal(5, bucket.FailedAttempts));
    }

    [PickupPostgresFact]
    public async Task ExpiryIsFixed_BlockedRequestsDoNotExtendIt_ThenCorrectCodeSucceeds()
    {
        for (int attempt = 0; attempt < 5; attempt++) await Record.ExceptionAsync(() => Verify());
        clock.Now = clock.Now.AddMinutes(14);
        var error = await Assert.ThrowsAsync<TooManyRequestsException>(() => Verify());
        Assert.Equal(60, error.RetryAfterSeconds);
        clock.Now = clock.Now.AddMinutes(1);
        var session = await Verify("123456");
        Assert.False(string.IsNullOrWhiteSpace(session.GuestSessionToken));
        await using var db = Context();
        Assert.Equal(0, (await db.Set<ReturnPickupAttempt>().SingleAsync()).FailedAttempts);
    }

    [PickupPostgresFact]
    public async Task SuccessResetsFailures_WithoutOpeningCompartment()
    {
        for (int attempt = 0; attempt < 4; attempt++) await Assert.ThrowsAsync<NotFoundException>(() => Verify());
        await Verify("123456"); // Unused physical access dependencies are null: any unexpected call fails this test.
        for (int attempt = 0; attempt < 4; attempt++) await Assert.ThrowsAsync<NotFoundException>(() => Verify());
        await using var db = Context();
        Assert.Equal(4, (await db.Set<ReturnPickupAttempt>().SingleAsync()).FailedAttempts);
        Assert.NotNull((await db.ReturnRequests.SingleAsync()).ShipperSessionTokenHash);
    }

    [PickupPostgresFact]
    public async Task BucketsIsolateLockerAndIp_AndNormalizeMappedIpv4()
    {
        for (int attempt = 0; attempt < 5; attempt++) await Record.ExceptionAsync(() => Verify());
        await Assert.ThrowsAsync<TooManyRequestsException>(() => Verify(ip: "::ffff:192.0.2.1"));
        await Verify("123456", ip: "192.0.2.2");
        await Assert.ThrowsAsync<NotFoundException>(() => Verify(locker: "PICKUP-B"));
        await using var db = Context();
        Assert.Equal(3, await db.Set<ReturnPickupAttempt>().CountAsync());
        Assert.All(await db.Set<ReturnPickupAttempt>().ToListAsync(), bucket => Assert.Equal(64, bucket.ClientKey.Length));
    }

    [PickupPostgresFact]
    public async Task MissingIpFailsClosed_UnknownLockerDoesNotAllocateBucket()
    {
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Verify(ip: null));
        Assert.Equal(ReturnErrorCodes.PickupVerificationUnavailable, error.ErrorCode);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => Verify(ip: "unknown"));
        await Assert.ThrowsAsync<NotFoundException>(() => Verify(locker: "NOT-EXIST"));
        await using var db = Context();
        Assert.Empty(await db.Set<ReturnPickupAttempt>().ToListAsync());
    }

    [PickupPostgresFact]
    public async Task UsedCodeFailuresAlsoReachLockout()
    {
        await using (var db = Context())
        {
            var entity = await db.ReturnRequests.SingleAsync();
            entity.Status = ReturnRequestStatus.Completed;
            await db.SaveChangesAsync();
        }
        for (int attempt = 0; attempt < 4; attempt++) await Assert.ThrowsAsync<ConflictException>(() => Verify("123456"));
        await Assert.ThrowsAsync<TooManyRequestsException>(() => Verify("123456"));
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);

    private async Task<ReturnPickupSessionResponse> Verify(string code = "000000", string locker = "PICKUP-A", string? ip = Ip)
    {
        await using var db = Context();
        var service = new ReturnRequestService(db, null!, null!, null!, new Sha256TokenHashService(), clock);
        return await service.CreateAsync(new ValidateReturnPickupRequest(locker, code), default, ip);
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

internal sealed class PickupPostgresFactAttribute : FactAttribute
{
    public PickupPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMART_LOCKING_TEST_POSTGRES")))
            Skip = "Set SMART_LOCKING_TEST_POSTGRES to a PostgreSQL administrator connection for disposable database tests.";
    }
}
