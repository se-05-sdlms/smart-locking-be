using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smart_locking_be.Application.DTOs.Auth;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Tests.Auth;

/// <summary>Runs against an isolated, disposable PostgreSQL database, never the configured application database.</summary>
public sealed class OtpPostgresTests : IAsyncLifetime
{
    private const string Phone = "0912345678";
    private const string Code = "123456";
    private readonly string databaseName = $"smart_locking_otp_test_{Guid.NewGuid():N}";
    private string connectionString = string.Empty;
    private string adminConnectionString = string.Empty;
    private readonly Guid lockerId = Guid.NewGuid();
    private readonly Guid challengeId = Guid.NewGuid();

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
        await db.Database.EnsureCreatedAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.AddRange(new Locker
        {
            Id = lockerId,
            Code = "OTP-TEST",
            Address = "Test",
            RecoveryAddress = "Test",
            DeviceIdentifier = "OTP-TEST",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = now,
            UpdatedAt = now
        }, new OtpChallenge
        {
            Id = challengeId,
            DestinationPhone = Phone,
            Purpose = OtpPurpose.Registration,
            CodeHash = new Sha256TokenHashService().HashToken(Code),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(10)
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(adminConnectionString)) return;
        // Only the random database allocated by this fixture is removed.
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task Registration_FiveFailuresRemainCommitted_AndCorrectSixthCodeIsRejected()
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            await using var db = Context();
            var error = await Assert.ThrowsAsync<BadRequestException>(() => Auth(db).RegisterAsync(Request("000000"), null, default));
            Assert.Equal(AuthErrorCodes.InvalidOtp, error.ErrorCode);
            await using var read = Context();
            Assert.Equal(attempt, (await read.OtpChallenges.SingleAsync()).AttemptCount);
        }

        await using (var db = Context())
            await Assert.ThrowsAsync<BadRequestException>(() => Auth(db).RegisterAsync(Request(Code), null, default));

        await using var verify = Context();
        var challenge = await verify.OtpChallenges.SingleAsync();
        Assert.Equal(5, challenge.AttemptCount);
        Assert.True(challenge.LockedUntil > DateTimeOffset.UtcNow.AddMinutes(14));
        Assert.Null(challenge.UsedAt);
        Assert.Empty(await verify.Users.ToListAsync());
        Assert.Empty(await verify.ResidentProfiles.ToListAsync());
        Assert.Empty(await verify.RefreshTokens.ToListAsync());
        Assert.Equal(1, await verify.AuditLogs.CountAsync(log => log.Action == "Auth.OtpLocked"));
    }

    [PostgresFact]
    public async Task ParallelFailures_AreNotLost_AndOnlyOneLockoutIsRecorded()
    {
        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = Context();
            await Assert.ThrowsAsync<BadRequestException>(() => Otp(db).VerifyAndConsumeAsync(Phone, "000000", OtpPurpose.Registration, null, default));
        }));
        await using var verify = Context();
        var challenge = await verify.OtpChallenges.SingleAsync();
        Assert.Equal(5, challenge.AttemptCount);
        Assert.True(challenge.LockedUntil > DateTimeOffset.UtcNow);
        Assert.Null(challenge.UsedAt);
        Assert.Equal(1, await verify.AuditLogs.CountAsync(log => log.Action == "Auth.OtpLocked"));
    }

    [PostgresFact]
    public async Task ParallelCorrectVerifications_ConsumeChallengeExactlyOnce()
    {
        bool[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = Context();
            try
            {
                await Otp(db).VerifyAndConsumeAsync(Phone, Code, OtpPurpose.Registration, null, default);
                return true;
            }
            catch (BadRequestException error) when (error.ErrorCode == AuthErrorCodes.InvalidOtp) { return false; }
        }));
        Assert.Single(results.Where(success => success));
        await using var verify = Context();
        Assert.NotNull((await verify.OtpChallenges.SingleAsync()).UsedAt);
    }

    [PostgresFact]
    public async Task SuccessfulRegistration_CommitsAccountProfileTokenAndConsumption()
    {
        await using (var db = Context())
            await Auth(db).RegisterAsync(Request(Code), null, default);
        await using var verify = Context();
        Assert.NotNull((await verify.OtpChallenges.SingleAsync()).UsedAt);
        Assert.Single(await verify.Users.ToListAsync());
        Assert.Single(await verify.ResidentProfiles.ToListAsync());
        Assert.Single(await verify.RefreshTokens.ToListAsync());
        Assert.Single(await verify.AuditLogs.Where(log => log.Action == "RegisterResident").ToListAsync());
    }

    [PostgresFact]
    public async Task AccountPersistenceFailure_RollsBackSuccessfulOtpConsumption()
    {
        await using (var db = Context(new FailAccountSaveInterceptor()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Auth(db).RegisterAsync(Request(Code), null, default));
        await using var verify = Context();
        Assert.Null((await verify.OtpChallenges.SingleAsync()).UsedAt);
        Assert.Empty(await verify.Users.ToListAsync());
        Assert.Empty(await verify.RefreshTokens.ToListAsync());
    }

    private RegisterRequest Request(string code) => new(Phone, code, "P@ssw0rd123", "OTP Test", lockerId, null);

    private ApplicationDbContext Context(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new ApplicationDbContext(builder.Options);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:Key"] = "test-secret-key-with-at-least-32-bytes",
        ["Jwt:Issuer"] = "smart-locking-be",
        ["Jwt:Audience"] = "smart-locking-clients"
    }).Build();

    private static OtpService Otp(ApplicationDbContext db) => new(db, new Sha256TokenHashService(),
        TimeProvider.System, Configuration(), NullLogger<OtpService>.Instance);

    private static AuthService Auth(ApplicationDbContext db) => new(db, new Pbkdf2PasswordHashService(),
        new Sha256TokenHashService(), new JwtTokenService(Configuration()), Otp(db), Configuration());

    private sealed class FailAccountSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<User>().Any(entry => entry.State == EntityState.Added))
                throw new InvalidOperationException("Simulated account persistence failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMART_LOCKING_TEST_POSTGRES")))
            Skip = "Set SMART_LOCKING_TEST_POSTGRES to a PostgreSQL account allowed to create isolated test databases. CI always sets it.";
    }
}
