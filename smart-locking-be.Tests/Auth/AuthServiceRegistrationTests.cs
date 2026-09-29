using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.DTOs.Auth;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Tests.Auth;

public sealed class AuthServiceRegistrationTests
{
    private const string OtpCode = "123456";

    [Fact]
    public async Task RegisterAsync_WithValidOtpAndLocker_CreatesCompleteResidentAccount()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        dbContext.AddRange(locker, CreateRegistrationChallenge());
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        AuthTokenResponse response = await service.RegisterAsync(
            CreateRequest(locker.Id),
            "127.0.0.1",
            default);

        User user = await dbContext.Users.Include(item => item.ResidentProfile).SingleAsync();
        Assert.Equal(response.User.Id, user.Id);
        Assert.Equal(UserRole.Resident, user.Role);
        Assert.Equal("0912345678", user.PhoneNumber);
        Assert.Equal(locker.Id, user.ResidentProfile!.RegisteredLockerId);
        Assert.Equal("Nguyen Van A", user.ResidentProfile.FullName);
        Assert.NotEmpty(user.ResidentProfile.PersonalQrTokenHash);
        Assert.NotNull((await dbContext.OtpChallenges.SingleAsync()).UsedAt);
        Assert.Equal(AuditLogResult.Succeeded, (await dbContext.AuditLogs.SingleAsync()).Result);
        Assert.Single(dbContext.RefreshTokens);
    }

    [Fact]
    public async Task RegisterAsync_WithInvalidOtp_DoesNotCreateAccount()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        dbContext.AddRange(locker, CreateRegistrationChallenge());
        await dbContext.SaveChangesAsync();

        RegisterRequest request = CreateRequest(locker.Id) with { OtpCode = "654321" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(dbContext).RegisterAsync(request, null, default));

        Assert.Empty(dbContext.Users);
        Assert.Empty(dbContext.ResidentProfiles);
        Assert.Empty(dbContext.RefreshTokens);
    }

    [Fact]
    public async Task RegisterAsync_WithInactiveLocker_RejectsBeforeConsumingOtp()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Inactive);
        OtpChallenge challenge = CreateRegistrationChallenge();
        dbContext.AddRange(locker, challenge);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(dbContext).RegisterAsync(CreateRequest(locker.Id), null, default));

        Assert.Null(challenge.UsedAt);
        Assert.Empty(dbContext.Users);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicatePhone_RejectsBeforeConsumingOtp()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        OtpChallenge challenge = CreateRegistrationChallenge();
        dbContext.AddRange(
            locker,
            challenge,
            new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = "0912345678",
                PasswordHash = "hash",
                Role = UserRole.Resident,
                Status = UserStatus.Active
            });
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(dbContext).RegisterAsync(CreateRequest(locker.Id), null, default));

        Assert.Null(challenge.UsedAt);
        Assert.Single(dbContext.Users);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithValidOtp_UpdatesPasswordAndAudits()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var passwordHashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = passwordHashService.HashPassword("old-password"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        OtpChallenge challenge = CreateRegistrationChallenge();
        challenge.UserId = user.Id;
        challenge.Purpose = OtpPurpose.PasswordReset;
        dbContext.AddRange(user, challenge);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).ResetPasswordAsync(
            new ResetPasswordRequest(user.PhoneNumber, OtpCode, "new-password"),
            "127.0.0.1",
            default);

        Assert.True(passwordHashService.VerifyPassword("new-password", user.PasswordHash));
        Assert.NotNull(challenge.UsedAt);
        AuditLog auditLog = await dbContext.AuditLogs.SingleAsync();
        Assert.Equal("ResetPassword", auditLog.Action);
        Assert.Equal(user.Id, auditLog.ActorUserId);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AuthService CreateService(ApplicationDbContext dbContext)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-secret-key-with-at-least-32-bytes",
                ["Jwt:Issuer"] = "smart-locking-be",
                ["Jwt:Audience"] = "smart-locking-clients",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "30"
            })
            .Build();
        var tokenHashService = new Sha256TokenHashService();
        var otpService = new OtpService(
            dbContext,
            tokenHashService,
            TimeProvider.System,
            configuration,
            NullLogger<OtpService>.Instance);

        return new AuthService(
            dbContext,
            new Pbkdf2PasswordHashService(),
            tokenHashService,
            new JwtTokenService(configuration),
            otpService,
            configuration);
    }

    private static RegisterRequest CreateRequest(Guid lockerId) =>
        new("0912345678", OtpCode, "password123", "Nguyen Van A", lockerId, null);

    private static Locker CreateLocker(LockerOperationalStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = $"LOCKER-{Guid.NewGuid():N}",
            Address = "123 Nguyen Van Linh",
            RecoveryAddress = "456 Nguyen Van Linh",
            DeviceIdentifier = $"DEVICE-{Guid.NewGuid():N}",
            OperationalStatus = status,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static OtpChallenge CreateRegistrationChallenge()
    {
        var tokenHashService = new Sha256TokenHashService();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.Registration,
            CodeHash = tokenHashService.HashToken(OtpCode),
            ExpiresAt = now.AddMinutes(10),
            CreatedAt = now
        };
    }
}
