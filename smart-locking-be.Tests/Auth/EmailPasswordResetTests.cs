using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.DTOs.Auth;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Tests.Auth;

public sealed class EmailPasswordResetTests
{
    [Theory]
    [InlineData(UserRole.Administrator, true)]
    [InlineData(UserRole.Administrator, false)]
    [InlineData(UserRole.LockerOperator, true)]
    [InlineData(UserRole.LockerOperator, false)]
    public async Task InternalEmailReset_IssuesAndConsumesEmailEvenWhenPhoneExists(UserRole role, bool hasPhone)
    {
        await using var db = Context();
        User user = await Seed(db, role, hasPhone);
        var transport = new Transport();
        AuthService service = Service(db, transport);
        const string identifier = "  OPERATOR@EXAMPLE.TEST  ";
        await service.RequestPasswordResetAsync(new(identifier), default);
        Assert.Equal("operator@example.test", transport.Email);
        Assert.Null(transport.Phone);
        OtpChallenge challenge = Assert.Single(await db.OtpChallenges.ToListAsync());
        Assert.Equal(user.Id, challenge.UserId);
        Assert.Equal(OtpPurpose.PasswordReset, challenge.Purpose);
        Assert.Equal(user.Email, challenge.DestinationEmail);
        Assert.Equal(string.Empty, challenge.DestinationPhone);

        await Assert.ThrowsAsync<BadRequestException>(() => service.ResetPasswordAsync(
            new(identifier, "invalid", "NewPassword123"), null, default));
        Assert.Equal("old-hash", user.PasswordHash);
        Assert.Null(challenge.UsedAt);

        await service.ResetPasswordAsync(new(identifier, transport.Code!, "NewPassword123"), null, default);
        Assert.True(new Pbkdf2PasswordHashService().VerifyPassword("NewPassword123", user.PasswordHash));
        Assert.False(user.MustChangePassword);
        Assert.NotNull(challenge.UsedAt);
        Assert.Single(await db.AuditLogs.Where(item => item.Action == "ResetPassword").ToListAsync());
        await Assert.ThrowsAsync<BadRequestException>(() => service.ResetPasswordAsync(
            new(identifier, transport.Code!, "AnotherPassword123"), null, default));
        Assert.True(new Pbkdf2PasswordHashService().VerifyPassword("NewPassword123", user.PasswordHash));
        Assert.Null(transport.Phone);
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.LockerOperator)]
    [InlineData(UserRole.Resident)]
    public async Task ExplicitPhoneIdentifier_RetainsSmsReset(UserRole role)
    {
        await using var db = Context();
        User user = await Seed(db, role, true);
        var transport = new Transport();
        AuthService service = Service(db, transport);
        await service.RequestPasswordResetAsync(new(user.PhoneNumber!), default);
        Assert.Equal(user.PhoneNumber, transport.Phone);
        Assert.Null(transport.Email);
        await service.ResetPasswordAsync(new(user.PhoneNumber!, transport.Code!, "NewPassword123"), null, default);
        Assert.NotNull(Assert.Single(await db.OtpChallenges.ToListAsync()).UsedAt);
        Assert.True(new Pbkdf2PasswordHashService().VerifyPassword("NewPassword123", user.PasswordHash));
    }

    [Fact]
    public async Task EmailIdentifier_RejectsSmsChallengeForSameInternalAccount()
    {
        await using var db = Context();
        User user = await Seed(db, UserRole.Administrator, true);
        var transport = new Transport();
        AuthService service = Service(db, transport);
        await service.RequestPasswordResetAsync(new(user.PhoneNumber!), default);
        await Assert.ThrowsAsync<BadRequestException>(() => service.ResetPasswordAsync(
            new(user.Email!, transport.Code!, "NewPassword123"), null, default));
        Assert.Equal("old-hash", user.PasswordHash);
        Assert.Null(Assert.Single(await db.OtpChallenges.ToListAsync()).UsedAt);
    }

    [Fact]
    public async Task MissingEmail_SendsNothingAndResetReturnsInvalidOtp()
    {
        await using var db = Context();
        var transport = new Transport();
        AuthService service = Service(db, transport);
        await service.RequestPasswordResetAsync(new("missing@example.test"), default);
        Assert.Null(transport.Email);
        Assert.Null(transport.Phone);
        Assert.Empty(await db.OtpChallenges.ToListAsync());
        await Assert.ThrowsAsync<BadRequestException>(() => service.ResetPasswordAsync(
            new("missing@example.test", "123456", "NewPassword123"), null, default));
    }

    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<User> Seed(ApplicationDbContext db, UserRole role, bool hasPhone)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "operator@example.test",
            PhoneNumber = hasPhone ? "0912345678" : null,
            PasswordHash = "old-hash",
            MustChangePassword = true,
            Role = role,
            Status = UserStatus.Active
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static AuthService Service(ApplicationDbContext db, Transport transport)
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var hash = new Sha256TokenHashService();
        var otp = new OtpService(db, hash, TimeProvider.System, configuration, NullLogger<OtpService>.Instance,
            transport, transport);
        return new AuthService(db, new Pbkdf2PasswordHashService(), hash, new JwtTokenService(configuration), otp, configuration);
    }

    // Only the external delivery boundary is replaced; AuthService and OTP lifecycle run normally.
    private sealed class Transport : IEmailSender, ISmsSender
    {
        public string? Email { get; private set; }
        public string? Phone { get; private set; }
        public string? Code { get; private set; }
        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
        {
            Email = to;
            Code = Regex.Match(body, @"\b\d{6}\b").Value;
            Assert.Equal(6, Code.Length);
            return Task.CompletedTask;
        }
        public Task SendOtpAsync(string destinationPhone, string code, string purpose, Guid messageId, CancellationToken cancellationToken)
        {
            Phone = destinationPhone;
            Code = code;
            return Task.CompletedTask;
        }
    }
}
