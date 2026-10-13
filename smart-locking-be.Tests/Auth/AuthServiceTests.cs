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
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "RegisterResident" && a.Result == AuditLogResult.Succeeded);
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
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            new RegisterRequest("", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            null,
            default));
        Assert.Contains("Phone number is required", ex.Message);
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
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            new RegisterRequest("091234567", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            null,
            default));
        Assert.Contains("Phone number must contain 10 digits and start with 0", ex.Message);
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
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            new RegisterRequest("09123456789", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            null,
            default));
        Assert.Contains("Phone number must contain 10 digits and start with 0", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            new RegisterRequest("091234567a", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            null,
            default));
        Assert.Contains("Phone number must contain 10 digits and start with 0", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        dbContext.AddRange(locker, CreateRegistrationChallenge("0912345678"));
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            new RegisterRequest("0912345678", OtpCode, "Pass123", "Nguyen Van A", locker.Id, null),
            null,
            default));
        Assert.Contains("Password must contain at least 8 characters", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        dbContext.AddRange(locker, CreateRegistrationChallenge("0912345678"));
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            new RegisterRequest("0912345678", OtpCode, "P@ssw0rd123", "", locker.Id, null),
            null,
            default));
        Assert.Contains("Full name must contain between 1 and 150 characters", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC08()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        dbContext.AddRange(locker, CreateRegistrationChallenge("0912345678"));
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            new RegisterRequest("0912345678", OtpCode, "P@ssw0rd123", new string('A', 151), locker.Id, null),
            null,
            default));
        Assert.Contains("Full name must contain between 1 and 150 characters", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC09()
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
        dbContext.AddRange(locker, existing, CreateRegistrationChallenge("0912345678"));
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            new RegisterRequest("0912345678", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            null,
            default));
        Assert.Equal("User already exists.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC10()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Inactive);
        dbContext.AddRange(locker, CreateRegistrationChallenge("0912345678"));
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            new RegisterRequest("0912345678", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            null,
            default));
        Assert.Equal("Registered locker is not operational.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC11()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        dbContext.AddRange(locker, CreateRegistrationChallenge("0912345678"));
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            new RegisterRequest("0912345678", "654321", "P@ssw0rd123", "Nguyen Van A", locker.Id, null),
            null,
            default));
        Assert.Equal("Invalid OTP.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC12()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        dbContext.Add(CreateRegistrationChallenge("0912345678"));
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RegisterAsync(
            new RegisterRequest("0912345678", OtpCode, "P@ssw0rd123", "Nguyen Van A", Guid.NewGuid(), null),
            null,
            default));
        Assert.Equal("Registered locker not found.", ex.Message);
        Assert.Empty(dbContext.Users.Where(u => u.PhoneNumber == "0912345678"));
        Assert.Empty(dbContext.ResidentProfiles);
        Assert.Empty(dbContext.RefreshTokens);
        Assert.Empty(dbContext.AuditLogs);
    }

    [Fact]
    [Trait("Category", "AUTH-01")]
    public async Task RegisterAsync_UTC13()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Locker locker = CreateLocker(LockerOperationalStatus.Operational);
        var existing = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0987654321",
            Email = "resident@example.com",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.AddRange(locker, existing, CreateRegistrationChallenge("0912345678"));
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            new RegisterRequest("0912345678", OtpCode, "P@ssw0rd123", "Nguyen Van A", locker.Id, "resident@example.com"),
            null,
            default));
        Assert.Equal("User already exists.", ex.Message);
        Assert.Empty(dbContext.Users.Where(u => u.PhoneNumber == "0912345678"));
        Assert.Empty(dbContext.ResidentProfiles);
        Assert.Empty(dbContext.RefreshTokens);
        Assert.Empty(dbContext.AuditLogs);
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

        var tokenHashService = new Sha256TokenHashService();
        Assert.Contains(dbContext.OtpChallenges, c => c.DestinationPhone == "0912345678" && c.Purpose == OtpPurpose.Registration);
    }

    [Fact]
    [Trait("Category", "AUTH-02")]
    public async Task RequestRegistrationOtpAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        dbContext.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        });
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await service.RequestRegistrationOtpAsync(new RequestRegistrationOtpRequest("0912345678"), default);

        Assert.Empty(dbContext.OtpChallenges);
    }

    [Fact]
    [Trait("Category", "AUTH-02")]
    public async Task RequestRegistrationOtpAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestRegistrationOtpAsync(new RequestRegistrationOtpRequest(""), default));
        Assert.Equal("Phone number is required.", ex.Message);
        Assert.Empty(dbContext.OtpChallenges);
    }

    [Fact]
    [Trait("Category", "AUTH-02")]
    public async Task RequestRegistrationOtpAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        // Current method does not validate format for RequestRegistrationOtpAsync directly,
        // it delegates to OtpService which throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RequestRegistrationOtpAsync(new RequestRegistrationOtpRequest("091234567a"), default));
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
            new LoginRequest("0912345678", "P@ssw0rd123"),
            "127.0.0.1",
            default);

        Assert.NotNull(response.AccessToken);
        Assert.NotNull(response.RefreshToken);
        Assert.NotNull(user.LastLoginAt);
        Assert.NotEqual(default, user.UpdatedAt);
        Assert.NotEmpty(dbContext.RefreshTokens);
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
            Email = "resident@example.com",
            PasswordHash = hashService.HashPassword("P@ssw0rd123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        AuthTokenResponse response = await service.LoginAsync(
            new LoginRequest("resident@example.com", "P@ssw0rd123"),
            "127.0.0.1",
            default);

        Assert.NotNull(response.AccessToken);
        Assert.NotNull(response.RefreshToken);
        Assert.NotNull(user.LastLoginAt);
        Assert.NotEqual(default, user.UpdatedAt);
        Assert.NotEmpty(dbContext.RefreshTokens);
    }

    [Fact]
    [Trait("Category", "AUTH-03")]
    public async Task LoginAsync_UTC03()
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
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoginAsync(new LoginRequest("0912345678", "wrong-password"), null, default));
        Assert.Equal("Invalid credentials.", ex.Message);
        Assert.Empty(dbContext.RefreshTokens);
    }

    [Fact]
    [Trait("Category", "AUTH-03")]
    public async Task LoginAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoginAsync(new LoginRequest("missing@example.com", "P@ssw0rd123"), null, default));
        Assert.Equal("Invalid credentials.", ex.Message);
        Assert.Empty(dbContext.RefreshTokens);
    }

    [Fact]
    [Trait("Category", "AUTH-03")]
    public async Task LoginAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("P@ssw0rd123"),
            Role = UserRole.Resident,
            Status = UserStatus.Locked
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoginAsync(new LoginRequest("0912345678", "P@ssw0rd123"), null, default));
        Assert.Equal("Invalid credentials.", ex.Message);
        Assert.Empty(dbContext.RefreshTokens);
    }

    [Fact]
    [Trait("Category", "AUTH-03")]
    public async Task LoginAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("P@ssw0rd123"),
            Role = UserRole.Resident,
            Status = UserStatus.Disabled
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoginAsync(new LoginRequest("0912345678", "P@ssw0rd123"), null, default));
        Assert.Equal("Invalid credentials.", ex.Message);
        Assert.Empty(dbContext.RefreshTokens);
    }
    #endregion

    #region AUTH-04: RefreshTokenAsync
    [Fact]
    [Trait("Category", "AUTH-04")]
    public async Task RefreshTokenAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var tokenHashService = new Sha256TokenHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = tokenHashService.HashToken("refresh-token-001"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(user, token);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        AuthTokenResponse response = await service.RefreshTokenAsync(
            new RefreshTokenRequest("refresh-token-001"),
            "127.0.0.1",
            default);

        Assert.NotNull(response.AccessToken);
        Assert.NotNull(token.RevokedAt);
        Assert.Equal("127.0.0.1", token.RevokedByIp);
        Assert.NotNull(token.ReplacedByTokenId);

        var replacementToken = await dbContext.RefreshTokens.FindAsync(token.ReplacedByTokenId);
        Assert.NotNull(replacementToken);
        Assert.Equal(user.Id, replacementToken.UserId);
        Assert.Null(replacementToken.RevokedAt);
    }

    [Fact]
    [Trait("Category", "AUTH-04")]
    public async Task RefreshTokenAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefreshTokenAsync(new RefreshTokenRequest("unknown-token"), null, default));
        Assert.Equal("Invalid refresh token.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-04")]
    public async Task RefreshTokenAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var tokenHashService = new Sha256TokenHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = tokenHashService.HashToken("refresh-token-001"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow,
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        dbContext.AddRange(user, token);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefreshTokenAsync(new RefreshTokenRequest("refresh-token-001"), null, default));
        Assert.Equal("Invalid refresh token.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-04")]
    public async Task RefreshTokenAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var tokenHashService = new Sha256TokenHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = tokenHashService.HashToken("refresh-token-001"),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        dbContext.AddRange(user, token);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefreshTokenAsync(new RefreshTokenRequest("refresh-token-001"), null, default));
        Assert.Equal("Invalid refresh token.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-04")]
    public async Task RefreshTokenAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var tokenHashService = new Sha256TokenHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = tokenHashService.HashToken("refresh-token-001"),
            ExpiresAt = now,
            CreatedAt = now.AddDays(-1)
        };
        dbContext.AddRange(user, token);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefreshTokenAsync(new RefreshTokenRequest("refresh-token-001"), null, default));
        Assert.Equal("Invalid refresh token.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-04")]
    public async Task RefreshTokenAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var tokenHashService = new Sha256TokenHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Disabled
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = tokenHashService.HashToken("refresh-token-001"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(user, token);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefreshTokenAsync(new RefreshTokenRequest("refresh-token-001"), null, default));
        Assert.Equal("Invalid refresh token.", ex.Message);
    }
    #endregion

    #region AUTH-05: LogoutAsync
    [Fact]
    [Trait("Category", "AUTH-05")]
    public async Task LogoutAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var tokenHashService = new Sha256TokenHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = tokenHashService.HashToken("refresh-token-001"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(user, token);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await service.LogoutAsync(new RefreshTokenRequest("refresh-token-001"), "127.0.0.1", default);

        Assert.NotNull(token.RevokedAt);
        Assert.Equal("127.0.0.1", token.RevokedByIp);
    }

    [Fact]
    [Trait("Category", "AUTH-05")]
    public async Task LogoutAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await service.LogoutAsync(new RefreshTokenRequest("unknown-token"), null, default);
        Assert.Empty(dbContext.RefreshTokens);
    }

    [Fact]
    [Trait("Category", "AUTH-05")]
    public async Task LogoutAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var tokenHashService = new Sha256TokenHashService();
        DateTimeOffset revokedTime = DateTimeOffset.UtcNow.AddHours(-1);
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = tokenHashService.HashToken("refresh-token-001"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedAt = revokedTime,
            RevokedByIp = "10.0.0.1"
        };
        dbContext.AddRange(user, token);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await service.LogoutAsync(new RefreshTokenRequest("refresh-token-001"), "127.0.0.1", default);

        Assert.Equal(revokedTime, token.RevokedAt);
        Assert.Equal("10.0.0.1", token.RevokedByIp);
    }

    [Fact]
    [Trait("Category", "AUTH-05")]
    public async Task LogoutAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await service.LogoutAsync(new RefreshTokenRequest(""), null, default);
        Assert.Empty(dbContext.RefreshTokens);
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
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PhoneNumber = "0912345678",
            Role = UserRole.Resident,
            Status = UserStatus.Active,
            PasswordHash = "hash"
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        UserProfileResponse profile = await service.GetCurrentUserAsync(user.Id, default);

        Assert.Equal(user.Id, profile.Id);
        Assert.Equal("0912345678", profile.PhoneNumber);
        Assert.Equal("Resident", profile.Role);
        Assert.Equal("Active", profile.Status);
    }

    [Fact]
    [Trait("Category", "AUTH-06")]
    public async Task GetCurrentUserAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetCurrentUserAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"), default));
        Assert.Equal("User not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-06")]
    public async Task GetCurrentUserAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetCurrentUserAsync(Guid.Empty, default));
        Assert.Equal("User not found.", ex.Message);
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

        Assert.Contains(dbContext.OtpChallenges, c => c.DestinationPhone == "0912345678" && c.Purpose == OtpPurpose.PasswordReset);
    }

    [Fact]
    [Trait("Category", "AUTH-07")]
    public async Task RequestPasswordResetAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            Email = "resident@example.com",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await service.RequestPasswordResetAsync(new RequestPasswordResetRequest("resident@example.com"), default);

        Assert.Contains(dbContext.OtpChallenges, c => c.DestinationPhone == "0912345678" && c.Purpose == OtpPurpose.PasswordReset);
    }

    [Fact]
    [Trait("Category", "AUTH-07")]
    public async Task RequestPasswordResetAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        await service.RequestPasswordResetAsync(new RequestPasswordResetRequest("missing@example.com"), default);
        Assert.Empty(dbContext.OtpChallenges);
    }

    [Fact]
    [Trait("Category", "AUTH-07")]
    public async Task RequestPasswordResetAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = null,
            Email = "nophone@example.com",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await service.RequestPasswordResetAsync(new RequestPasswordResetRequest("nophone@example.com"), default);

        Assert.Empty(dbContext.OtpChallenges);
    }
    #endregion

    #region AUTH-08: ResetPasswordAsync
    [Fact]
    [Trait("Category", "AUTH-08")]
    public async Task ResetPasswordAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var tokenHashService = new Sha256TokenHashService();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("OldPass123"),
            MustChangePassword = true,
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var otp = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.PasswordReset,
            CodeHash = tokenHashService.HashToken("123456"),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(user, otp);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await service.ResetPasswordAsync(new ResetPasswordRequest("0912345678", "123456", "NewPass123"), "127.0.0.1", default);

        Assert.False(user.MustChangePassword);
        Assert.True(hashService.VerifyPassword("NewPass123", user.PasswordHash));
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "ResetPassword" && a.Result == AuditLogResult.Succeeded);
    }

    [Fact]
    [Trait("Category", "AUTH-08")]
    public async Task ResetPasswordAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequest("missing@example.com", "123456", "NewPass123"), null, default));
        Assert.Equal("Invalid OTP.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-08")]
    public async Task ResetPasswordAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = null,
            Email = "nophone@example.com",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequest("nophone@example.com", "123456", "NewPass123"), null, default));
        Assert.Equal("Invalid OTP.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-08")]
    public async Task ResetPasswordAsync_UTC04()
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
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequest("0912345678", "123456", "Pass123"), null, default));
        Assert.Contains("Password must contain at least 8 characters", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-08")]
    public async Task ResetPasswordAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var tokenHashService = new Sha256TokenHashService();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("OldPass123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var otp = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.PasswordReset,
            CodeHash = tokenHashService.HashToken("123456"),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(user, otp);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        await service.ResetPasswordAsync(new ResetPasswordRequest("0912345678", "123456", "Pass1234"), null, default);

        Assert.True(hashService.VerifyPassword("Pass1234", user.PasswordHash));
    }

    [Fact]
    [Trait("Category", "AUTH-08")]
    public async Task ResetPasswordAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var tokenHashService = new Sha256TokenHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var otp = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.PasswordReset,
            CodeHash = tokenHashService.HashToken("123456"),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(user, otp);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        int initialAuditLogs = dbContext.AuditLogs.Count();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequest("0912345678", "654321", "NewPass123"), null, default));
        Assert.Equal("Invalid OTP.", ex.Message);
        Assert.Equal("hash", user.PasswordHash);
        Assert.Equal(initialAuditLogs, dbContext.AuditLogs.Count());
    }
    #endregion

    #region AUTH-09: ChangePasswordAsync
    [Fact]
    [Trait("Category", "AUTH-09")]
    public async Task ChangePasswordAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var tokenHashService = new Sha256TokenHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("OldPass123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        var token1 = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = tokenHashService.HashToken("token-01"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var token2 = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = tokenHashService.HashToken("token-02"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        };
        DateTimeOffset pastRevokedDate = DateTimeOffset.UtcNow.AddDays(-2);
        var pastRevokedToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = tokenHashService.HashToken("token-03"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            RevokedAt = pastRevokedDate,
            RevokedByIp = "10.0.0.1"
        };
        dbContext.AddRange(user, token1, token2, pastRevokedToken);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        AuthTokenResponse response = await service.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest("OldPass123", "NewPass123"),
            "127.0.0.1",
            default);

        Assert.NotNull(response.AccessToken);
        Assert.True(hashService.VerifyPassword("NewPass123", user.PasswordHash));
        Assert.NotNull(token1.RevokedAt);
        Assert.Equal("127.0.0.1", token1.RevokedByIp);
        Assert.NotNull(token2.RevokedAt);
        Assert.Equal("127.0.0.1", token2.RevokedByIp);
        Assert.Equal(pastRevokedDate, pastRevokedToken.RevokedAt);
        Assert.Equal("10.0.0.1", pastRevokedToken.RevokedByIp);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "Auth.PasswordChanged" && a.Result == AuditLogResult.Succeeded);
    }

    [Fact]
    [Trait("Category", "AUTH-09")]
    public async Task ChangePasswordAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ChangePasswordAsync(Guid.NewGuid(), new ChangePasswordRequest("OldPass123", "NewPass123"), null, default));
        Assert.Equal("User not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-09")]
    public async Task ChangePasswordAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        AuthService service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ChangePasswordAsync(Guid.Empty, new ChangePasswordRequest("OldPass123", "NewPass123"), null, default));
        Assert.Equal("User not found.", ex.Message);
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
            PasswordHash = hashService.HashPassword("OldPass123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChangePasswordAsync(user.Id, new ChangePasswordRequest("wrong-password", "NewPass123"), null, default));
        Assert.Equal("Current password is incorrect.", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-09")]
    public async Task ChangePasswordAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("OldPass123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ChangePasswordAsync(user.Id, new ChangePasswordRequest("OldPass123", "Pass123"), null, default));
        Assert.Contains("Password must contain at least 8 characters", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-09")]
    public async Task ChangePasswordAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("OldPass123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ChangePasswordAsync(user.Id, new ChangePasswordRequest("OldPass123", "OldPass123"), null, default));
        Assert.Contains("New password must be different from the current password", ex.Message);
    }

    [Fact]
    [Trait("Category", "AUTH-09")]
    public async Task ChangePasswordAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var hashService = new Pbkdf2PasswordHashService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0912345678",
            PasswordHash = hashService.HashPassword("OldPass123"),
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        AuthService service = CreateService(dbContext);
        AuthTokenResponse response = await service.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest("OldPass123", "Pass1234"),
            "127.0.0.1",
            default);

        Assert.NotNull(response.AccessToken);
        Assert.True(hashService.VerifyPassword("Pass1234", user.PasswordHash));
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
