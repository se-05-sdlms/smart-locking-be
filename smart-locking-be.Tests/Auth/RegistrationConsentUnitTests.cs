using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.DTOs.Auth;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Auth;

public sealed class RegistrationConsentUnitTests
{
    private const string OtpCode = "123456";
    private const string RawPassword = "P@ssword123";

    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AuthService CreateAuthService(ApplicationDbContext dbContext)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-secret-key-with-at-least-32-bytes-length",
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
            NullLogger<OtpService>.Instance,
            new FakeEmailSender());

        return new AuthService(
            dbContext,
            new Pbkdf2PasswordHashService(),
            tokenHashService,
            new JwtTokenService(configuration),
            otpService,
            configuration);
    }

    private static Locker SeedLocker(ApplicationDbContext dbContext)
    {
        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = $"LOCKER-{Guid.NewGuid():N}",
            Address = "123 Test Street",
            RecoveryAddress = "456 Recovery Street",
            DeviceIdentifier = $"DEVICE-{Guid.NewGuid():N}",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Lockers.Add(locker);
        return locker;
    }

    private static OtpChallenge SeedRegistrationOtp(ApplicationDbContext dbContext, string phone)
    {
        var tokenHash = new Sha256TokenHashService();
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationEmail = "resident@example.com",
            CodeHash = tokenHash.HashToken(OtpCode),
            Purpose = OtpPurpose.Registration,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.OtpChallenges.Add(challenge);
        return challenge;
    }

    private static RegisterRequest BuildRegisterRequest(
        Guid lockerId,
        string? termsVersion = "1.0",
        string? privacyVersion = "1.0",
        DateTimeOffset? consentedAt = null,
        string phone = "0912345678")
    {
        return new RegisterRequest(
            PhoneNumber: phone,
            OtpCode: OtpCode,
            Password: RawPassword,
            FullName: "Test Resident",
            RegisteredLockerId: lockerId,
            Email: "resident@example.com",
            TermsVersion: termsVersion,
            PrivacyVersion: privacyVersion,
            ConsentedAt: consentedAt ?? DateTimeOffset.UtcNow);
    }

    #region Consent Terms Version Validation
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0.9")]
    [InlineData("2.0")]
    [InlineData("v1.0")]
    [InlineData("1.0.1")]
    public async Task RegisterAsync_InvalidTermsVersion_ThrowsBadRequestException(string? invalidTerms)
    {
        await using var db = CreateInMemoryDbContext();
        var locker = SeedLocker(db);
        SeedRegistrationOtp(db, "0912345678");
        await db.SaveChangesAsync();

        var service = CreateAuthService(db);
        var request = BuildRegisterRequest(locker.Id, termsVersion: invalidTerms);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.RegisterAsync(request, "127.0.0.1", default));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("termsVersion"),
            $"Expected errors to contain 'termsVersion' for value '{invalidTerms}'.");
        Assert.Empty(await db.Users.ToListAsync());
    }
    #endregion

    #region Consent Privacy Policy Version Validation
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0.9")]
    [InlineData("2.0")]
    [InlineData("v1.0")]
    [InlineData("1.0.1")]
    public async Task RegisterAsync_InvalidPrivacyVersion_ThrowsBadRequestException(string? invalidPrivacy)
    {
        await using var db = CreateInMemoryDbContext();
        var locker = SeedLocker(db);
        SeedRegistrationOtp(db, "0912345678");
        await db.SaveChangesAsync();

        var service = CreateAuthService(db);
        var request = BuildRegisterRequest(locker.Id, privacyVersion: invalidPrivacy);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.RegisterAsync(request, "127.0.0.1", default));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("privacyVersion"),
            $"Expected errors to contain 'privacyVersion' for value '{invalidPrivacy}'.");
        Assert.Empty(await db.Users.ToListAsync());
    }
    #endregion

    #region ConsentedAt Timestamp Validation
    [Fact]
    public async Task RegisterAsync_NullConsentedAt_ThrowsBadRequestException()
    {
        await using var db = CreateInMemoryDbContext();
        var locker = SeedLocker(db);
        SeedRegistrationOtp(db, "0912345678");
        await db.SaveChangesAsync();

        var service = CreateAuthService(db);
        var request = new RegisterRequest(
            PhoneNumber: "0912345678",
            OtpCode: OtpCode,
            Password: RawPassword,
            FullName: "Test Resident",
            RegisteredLockerId: locker.Id,
            Email: "resident@example.com",
            TermsVersion: "1.0",
            PrivacyVersion: "1.0",
            ConsentedAt: null);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.RegisterAsync(request, "127.0.0.1", default));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("consentedAt"));
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task RegisterAsync_ConsentedAtMoreThan5MinutesInFuture_ThrowsBadRequestException()
    {
        await using var db = CreateInMemoryDbContext();
        var locker = SeedLocker(db);
        SeedRegistrationOtp(db, "0912345678");
        await db.SaveChangesAsync();

        var service = CreateAuthService(db);
        DateTimeOffset futureTimestamp = DateTimeOffset.UtcNow.AddMinutes(5).AddSeconds(30);
        var request = BuildRegisterRequest(locker.Id, consentedAt: futureTimestamp);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.RegisterAsync(request, "127.0.0.1", default));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("consentedAt"));
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Theory]
    [InlineData(0)]     // exactly now
    [InlineData(-60)]   // 1 hour in the past
    [InlineData(240)]   // 4 minutes in the future (within 5-minute tolerance)
    public async Task RegisterAsync_ConsentedAtWithinToleranceOrPast_Succeeds(int offsetSeconds)
    {
        string phone = $"09{Random.Shared.Next(10000000, 99999999)}";
        await using var db = CreateInMemoryDbContext();
        var locker = SeedLocker(db);
        SeedRegistrationOtp(db, phone);
        await db.SaveChangesAsync();

        var service = CreateAuthService(db);
        DateTimeOffset validTimestamp = DateTimeOffset.UtcNow.AddSeconds(offsetSeconds);
        var request = BuildRegisterRequest(locker.Id, consentedAt: validTimestamp, phone: phone);

        var response = await service.RegisterAsync(request, "127.0.0.1", default);

        Assert.NotEmpty(response.AccessToken);
        Assert.NotEmpty(response.RefreshToken);
        var persisted = await db.Users.SingleAsync(u => u.PhoneNumber == phone);
        Assert.Equal("1.0", persisted.TermsVersion);
        Assert.Equal("1.0", persisted.PrivacyVersion);
        Assert.NotNull(persisted.ConsentedAt);
    }
    #endregion

    #region Valid Consent Assignment & Persistence
    [Fact]
    public async Task RegisterAsync_ValidConsentWithWhitespace_TrimsAndPersistsCorrectly()
    {
        string phone = "0987654321";
        await using var db = CreateInMemoryDbContext();
        var locker = SeedLocker(db);
        SeedRegistrationOtp(db, phone);
        await db.SaveChangesAsync();

        var service = CreateAuthService(db);
        DateTimeOffset clientConsentTime = DateTimeOffset.UtcNow.AddMinutes(-2);
        var request = BuildRegisterRequest(
            locker.Id,
            termsVersion: "  1.0  ",
            privacyVersion: "  1.0  ",
            consentedAt: clientConsentTime,
            phone: phone);

        DateTimeOffset beforeCall = DateTimeOffset.UtcNow;
        var response = await service.RegisterAsync(request, "127.0.0.1", default);
        DateTimeOffset afterCall = DateTimeOffset.UtcNow;

        Assert.NotEmpty(response.AccessToken);
        Assert.NotEmpty(response.RefreshToken);
        Assert.Equal(phone, response.User.PhoneNumber);

        var persisted = await db.Users.SingleAsync(u => u.PhoneNumber == phone);
        Assert.Equal("1.0", persisted.TermsVersion);
        Assert.Equal("1.0", persisted.PrivacyVersion);
        Assert.NotNull(persisted.ConsentedAt);

        // Server UTC clock should be used for persistence
        Assert.InRange(persisted.ConsentedAt.Value, beforeCall.AddSeconds(-1), afterCall.AddSeconds(1));
    }
    #endregion

    #region Legacy User Handling (No Consent Recorded)
    [Fact]
    public async Task LegacyUser_WithoutConsent_CanLoginSuccessfully()
    {
        await using var db = CreateInMemoryDbContext();
        var legacyUser = SeedLegacyResident(db, "0901234567");
        await db.SaveChangesAsync();

        var service = CreateAuthService(db);
        var loginRequest = new LoginRequest("0901234567", RawPassword);

        var response = await service.LoginAsync(loginRequest, "127.0.0.1", default);

        Assert.NotEmpty(response.AccessToken);
        Assert.NotEmpty(response.RefreshToken);
        Assert.Equal(legacyUser.Id, response.User.Id);

        // Verify legacy user's consent fields remain null without crashing
        var reloaded = await db.Users.SingleAsync(u => u.Id == legacyUser.Id);
        Assert.Null(reloaded.TermsVersion);
        Assert.Null(reloaded.PrivacyVersion);
        Assert.Null(reloaded.ConsentedAt);
    }

    [Fact]
    public async Task LegacyUser_WithoutConsent_CanChangePasswordSuccessfully()
    {
        await using var db = CreateInMemoryDbContext();
        var legacyUser = SeedLegacyResident(db, "0902345678");
        await db.SaveChangesAsync();

        var service = CreateAuthService(db);
        var changeRequest = new ChangePasswordRequest(RawPassword, "NewValidPass@456");

        var response = await service.ChangePasswordAsync(legacyUser.Id, changeRequest, "127.0.0.1", default);

        Assert.NotEmpty(response.AccessToken);
        Assert.NotEmpty(response.RefreshToken);

        var reloaded = await db.Users.SingleAsync(u => u.Id == legacyUser.Id);
        Assert.Null(reloaded.TermsVersion);
        Assert.Null(reloaded.PrivacyVersion);
        Assert.Null(reloaded.ConsentedAt);

        // Verify password changed
        var hashService = new Pbkdf2PasswordHashService();
        Assert.True(hashService.VerifyPassword("NewValidPass@456", reloaded.PasswordHash));
    }

    [Fact]
    public async Task LegacyUser_WithoutConsent_CanRefreshTokenSuccessfully()
    {
        await using var db = CreateInMemoryDbContext();
        var legacyUser = SeedLegacyResident(db, "0903456789");
        var tokenHash = new Sha256TokenHashService();
        string rawRefreshToken = Guid.NewGuid().ToString("N");
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = legacyUser.Id,
            TokenHash = tokenHash.HashToken(rawRefreshToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync();

        var service = CreateAuthService(db);
        var refreshRequest = new RefreshTokenRequest(rawRefreshToken);

        var response = await service.RefreshTokenAsync(refreshRequest, "127.0.0.1", default);

        Assert.NotEmpty(response.AccessToken);
        Assert.NotEmpty(response.RefreshToken);

        var reloaded = await db.Users.SingleAsync(u => u.Id == legacyUser.Id);
        Assert.Null(reloaded.TermsVersion);
        Assert.Null(reloaded.PrivacyVersion);
        Assert.Null(reloaded.ConsentedAt);
    }

    [Fact]
    public async Task LegacyUser_WithoutConsent_CanBeRetrievedAndUpdatedViaUserService()
    {
        await using var db = CreateInMemoryDbContext();
        Guid adminId = Guid.NewGuid();
        var legacyOperator = new User
        {
            Id = Guid.NewGuid(),
            Email = "legacy.operator@example.com",
            PhoneNumber = null,
            PasswordHash = new Pbkdf2PasswordHashService().HashPassword("OpPass123!"),
            Role = UserRole.LockerOperator,
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            TermsVersion = null,
            PrivacyVersion = null,
            ConsentedAt = null,
            ResidentProfile = new ResidentProfile
            {
                Id = Guid.NewGuid(),
                FullName = "Legacy Operator",
                DeliveryApprovalMode = DeliveryApprovalMode.Manual,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30)
            }
        };
        db.Users.Add(legacyOperator);
        await db.SaveChangesAsync();

        var userService = new UserService(db, new Pbkdf2PasswordHashService(), new FakeEmailSender());

        // 1. GetUserByIdAsync should not crash and should return user details
        var userDetail = await userService.GetUserByIdAsync(legacyOperator.Id);
        Assert.Equal("legacy.operator@example.com", userDetail.Email);
        Assert.Equal("Legacy Operator", userDetail.FullName);

        // 2. UpdateUserAsync should update user profile without requiring consent fields
        var updateRequest = new UpdateUserRequest("Updated Legacy Operator", null, null);
        var updatedDetail = await userService.UpdateUserAsync(adminId, legacyOperator.Id, updateRequest);
        Assert.Equal("Updated Legacy Operator", updatedDetail.FullName);

        var reloaded = await db.Users.SingleAsync(u => u.Id == legacyOperator.Id);
        Assert.Null(reloaded.TermsVersion);
        Assert.Null(reloaded.PrivacyVersion);
        Assert.Null(reloaded.ConsentedAt);
    }
    #endregion

    private static User SeedLegacyResident(ApplicationDbContext db, string phone)
    {
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            Email = null,
            PasswordHash = hashService.HashPassword(RawPassword),
            Role = UserRole.Resident,
            Status = UserStatus.Active,
            MustChangePassword = false,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-3),
            UpdatedAt = DateTimeOffset.UtcNow.AddMonths(-3),
            TermsVersion = null,
            PrivacyVersion = null,
            ConsentedAt = null,
            ResidentProfile = new ResidentProfile
            {
                Id = Guid.NewGuid(),
                FullName = "Legacy Resident",
                DeliveryApprovalMode = DeliveryApprovalMode.Manual,
                CreatedAt = DateTimeOffset.UtcNow.AddMonths(-3),
                UpdatedAt = DateTimeOffset.UtcNow.AddMonths(-3)
            }
        };
        db.Users.Add(user);
        return user;
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
