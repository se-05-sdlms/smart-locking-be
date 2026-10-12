using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.DTOs.Auth;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Auth;

public sealed class AuthServiceTests
{
    private const string OtpCode = "123456";

    #region AUTH-01: RegisterAsync
    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        dbContext.AddRange(locker, CreateRegistrationChallenge("0912345678"));
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        AuthTokenResponse response = await service.RegisterAsync(
            new RegisterRequest("0912345678", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            "127.0.0.1",
            default);

        Assert.NotNull(response.AccessToken);
        Assert.Equal("0912345678", response.User.PhoneNumber);
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            new RegisterRequest("", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            null,
            default));
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            new RegisterRequest("091234567", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            null,
            default));
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            new RegisterRequest("09123456789", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            null,
            default));
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        var existing = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.AddRange(locker, existing);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            new RegisterRequest("0912345678", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            null,
            default));
    }
    #endregion

    #region AUTH-02: RequestRegistrationOtpAsync
    [Fact]
    [Trait("Category", "AUTH-02")]
    public async Task RequestRegistrationOtpAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await service.RequestRegistrationOtpAsync(new RequestRegistrationOtpRequest("0912345678"), default);

        Assert.Single(dbContext.OtpChallenges);
    }

    [Fact]
    [Trait("Category", "AUTH-02")]
    public async Task RequestRegistrationOtpAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestRegistrationOtpAsync(new RequestRegistrationOtpRequest(""), default));
    }

    [Fact]
    [Trait("Category", "AUTH-02")]
    public async Task RequestRegistrationOtpAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var existing = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(existing);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await service.RequestRegistrationOtpAsync(new RequestRegistrationOtpRequest("0912345678"), default);

        Assert.Empty(dbContext.OtpChallenges);
    }
    #endregion

    #region AUTH-03: LoginAsync
    [Fact]
    [Trait("Category", "AUTH-03")]
    public async Task LoginAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("P@ssw0rd123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        AuthTokenResponse response = await service.LoginAsync(
            new LoginRequest("0912345678", "P@ssw0rd123"), "127.0.0.1", default);

        Assert.NotNull(response.AccessToken);
        Assert.NotNull(response.RefreshToken);
        Assert.Equal(user.Id, response.User.Id);
    }

    [Fact]
    [Trait("Category", "AUTH-03")]
    public async Task LoginAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("P@ssw0rd123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoginAsync(new LoginRequest("0912345678", "WrongPass"), null, default));
    }

    [Fact]
    [Trait("Category", "AUTH-03")]
    public async Task LoginAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoginAsync(new LoginRequest("0999999999", "P@ssw0rd123"), null, default));
    }
    #endregion

    #region AUTH-04: RefreshTokenAsync
    [Fact]
    [Trait("Category", "AUTH-04")]
    public async Task RefreshTokenAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var tokenHashService = new Sha256TokenHashService();
        string rawToken = "raw-refresh-token-value";
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHashService.HashToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(user, refreshToken);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        AuthTokenResponse response = await service.RefreshTokenAsync(
            new RefreshTokenRequest(rawToken), "127.0.0.1", default);

        Assert.NotNull(response.AccessToken);
        Assert.NotNull(response.RefreshToken);
    }

    [Fact]
    [Trait("Category", "AUTH-04")]
    public async Task RefreshTokenAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefreshTokenAsync(new RefreshTokenRequest("non-existent-token"), null, default));
    }

    [Fact]
    [Trait("Category", "AUTH-04")]
    public async Task RefreshTokenAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var tokenHashService = new Sha256TokenHashService();
        string rawToken = "expired-token";
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHashService.HashToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        dbContext.AddRange(user, refreshToken);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefreshTokenAsync(new RefreshTokenRequest(rawToken), null, default));
    }
    #endregion

    #region AUTH-05: LogoutAsync
    [Fact]
    [Trait("Category", "AUTH-05")]
    public async Task LogoutAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var tokenHashService = new Sha256TokenHashService();
        string rawToken = "valid-token";
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHashService.HashToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(user, refreshToken);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await service.LogoutAsync(new RefreshTokenRequest(rawToken), "127.0.0.1", default);

        Assert.NotNull(refreshToken.RevokedAt);
    }

    [Fact]
    [Trait("Category", "AUTH-05")]
    public async Task LogoutAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        // Does not throw if token not found
        await service.LogoutAsync(new RefreshTokenRequest("non-existent"), null, default);
    }

    [Fact]
    [Trait("Category", "AUTH-05")]
    public async Task LogoutAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await service.LogoutAsync(new RefreshTokenRequest(""), null, default);
    }
    #endregion

    #region AUTH-06: GetCurrentUserAsync
    [Fact]
    [Trait("Category", "AUTH-06")]
    public async Task GetCurrentUserAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        UserProfileResponse profile = await service.GetCurrentUserAsync(user.Id, default);

        Assert.Equal(user.Id, profile.Id);
        Assert.Equal("0912345678", profile.PhoneNumber);
    }

    [Fact]
    [Trait("Category", "AUTH-06")]
    public async Task GetCurrentUserAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetCurrentUserAsync(Guid.NewGuid(), default));
    }

    [Fact]
    [Trait("Category", "AUTH-06")]
    public async Task GetCurrentUserAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetCurrentUserAsync(Guid.Empty, default));
    }
    #endregion

    #region AUTH-07: RequestPasswordResetAsync
    [Fact]
    [Trait("Category", "AUTH-07")]
    public async Task RequestPasswordResetAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await service.RequestPasswordResetAsync(new RequestPasswordResetRequest("0912345678"), default);

        Assert.Single(dbContext.OtpChallenges);
    }

    [Fact]
    [Trait("Category", "AUTH-07")]
    public async Task RequestPasswordResetAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        // Unknown identifier does not issue OTP
        await service.RequestPasswordResetAsync(new RequestPasswordResetRequest("0999999999"), default);
        Assert.Empty(dbContext.OtpChallenges);
    }

    [Fact]
    [Trait("Category", "AUTH-07")]
    public async Task RequestPasswordResetAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await service.RequestPasswordResetAsync(new RequestPasswordResetRequest(""), default);
        Assert.Empty(dbContext.OtpChallenges);
    }
    #endregion

    #region AUTH-08: ResetPasswordAsync
    [Fact]
    [Trait("Category", "AUTH-08")]
    public async Task ResetPasswordAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "old-hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var tokenHashService = new Sha256TokenHashService();
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DestinationPhone = user.PhoneNumber,
            Purpose = OtpPurpose.PasswordReset,
            CodeHash = tokenHashService.HashToken(OtpCode),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(user, challenge);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await service.ResetPasswordAsync(
            new ResetPasswordRequest("0912345678", OtpCode, "NewP@ssw0rd123"), "127.0.0.1", default);

        Assert.NotNull(challenge.UsedAt);
    }

    [Fact]
    [Trait("Category", "AUTH-08")]
    public async Task ResetPasswordAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequest("0999999999", OtpCode, "NewP@ssw0rd123"), null, default));
    }

    [Fact]
    [Trait("Category", "AUTH-08")]
    public async Task ResetPasswordAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "old-hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequest("0912345678", OtpCode, "short"), null, default));
    }
    #endregion

    #region AUTH-09: ChangePasswordAsync
    [Fact]
    [Trait("Category", "AUTH-09")]
    public async Task ChangePasswordAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("CurrentP@ss123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        AuthTokenResponse response = await service.ChangePasswordAsync(
            user.Id, new ChangePasswordRequest("CurrentP@ss123", "NewP@ssw0rd456"), "127.0.0.1", default);

        Assert.NotNull(response.AccessToken);
    }

    [Fact]
    [Trait("Category", "AUTH-09")]
    public async Task ChangePasswordAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("CurrentP@ss123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChangePasswordAsync(user.Id, new ChangePasswordRequest("WrongPass", "NewP@ssw0rd456"), null, default));
    }

    [Fact]
    [Trait("Category", "AUTH-09")]
    public async Task ChangePasswordAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("CurrentP@ss123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ChangePasswordAsync(user.Id, new ChangePasswordRequest("CurrentP@ss123", "short"), null, default));
    }

    [Fact]
    [Trait("Category", "AUTH-09")]
    public async Task ChangePasswordAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("CurrentP@ss123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ChangePasswordAsync(user.Id, new ChangePasswordRequest("CurrentP@ss123", "CurrentP@ss123"), null, default));
    }
    #endregion

    #region Helpers
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

    private static OtpChallenge CreateRegistrationChallenge(string phone)
    {
        var tokenHashService = new Sha256TokenHashService();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationPhone = phone,
            Purpose = OtpPurpose.Registration,
            CodeHash = tokenHashService.HashToken(OtpCode),
            ExpiresAt = now.AddMinutes(10),
            CreatedAt = now
        };
    }
    #endregion
}
