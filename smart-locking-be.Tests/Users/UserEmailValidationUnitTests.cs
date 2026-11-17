using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Users;

public sealed class UserEmailValidationUnitTests
{
    private readonly Guid _adminId = Guid.NewGuid();

    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static UserService CreateService(ApplicationDbContext dbContext, FakeEmailSender? emailSender = null)
    {
        return new UserService(
            dbContext,
            new FakePasswordHashService(),
            emailSender ?? new FakeEmailSender());
    }

    private static CreateUserRequest CreateRequest(string email, Guid? lockerId = null) =>
        new("Test Operator", email, null, UserRole.LockerOperator, null, lockerId, "Test Assignment");

    #region CreateUserAsync - Valid Emails
    [Theory]
    [InlineData("user@example.com", "user@example.com")]
    [InlineData("  user@example.com  ", "user@example.com")]
    [InlineData("user+tag@domain.com", "user+tag@domain.com")]
    [InlineData("sub.user@domain.vn", "sub.user@domain.vn")]
    [InlineData("First.Last+Ops@Example.Test", "first.last+ops@example.test")]
    [InlineData("operator@sub.example.test", "operator@sub.example.test")]
    [InlineData("operator@xn--bcher-kva.example", "operator@xn--bcher-kva.example")]
    public async Task CreateUserAsync_WithValidEmails_NormalizesAndSavesUserSuccessfully(string inputEmail, string expectedNormalized)
    {
        await using var db = CreateInMemoryDbContext();
        var emailSender = new FakeEmailSender();
        var service = CreateService(db, emailSender);

        var response = await service.CreateUserAsync(_adminId, CreateRequest(inputEmail));

        Assert.Equal(expectedNormalized, response.Email);
        var persisted = await db.Users.SingleAsync(u => u.Id == response.Id);
        Assert.Equal(expectedNormalized, persisted.Email);
        Assert.Equal(UserStatus.Active, persisted.Status);
        Assert.Equal(1, emailSender.SentCount);
    }

    [Fact]
    public async Task CreateUserAsync_WithMaxAllowedLength_SavesSuccessfully()
    {
        // 64 chars local-part + 1 char '@' + 63 + 1 + 63 + 1 + 61 = 254 chars total
        string maxValidEmail = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 61);
        Assert.Equal(254, maxValidEmail.Length);

        await using var db = CreateInMemoryDbContext();
        var service = CreateService(db);

        var response = await service.CreateUserAsync(_adminId, CreateRequest(maxValidEmail));

        Assert.Equal(maxValidEmail.ToLowerInvariant(), response.Email);
        var persisted = await db.Users.SingleAsync(u => u.Id == response.Id);
        Assert.Equal(maxValidEmail.ToLowerInvariant(), persisted.Email);
    }
    #endregion

    #region CreateUserAsync - Invalid Emails
    [Theory]
    [InlineData("user.@example.com")]               // trailing dot in local-part
    [InlineData(".user@example.com")]               // leading dot in local-part
    [InlineData("a@@example.test")]                 // double @
    [InlineData("a b@example.test")]                // space in local-part
    [InlineData("userexample.com")]                 // missing @
    [InlineData("user@")]                           // missing domain
    [InlineData("@example.com")]                    // missing local-part
    [InlineData("user@domain.")]                    // domain ends with dot
    [InlineData("user@.domain.com")]                // domain starts with dot
    [InlineData("user@domain..com")]                // consecutive dots in domain
    [InlineData("user@dom_ain.com")]                // invalid character '_' in domain
    [InlineData("user@domain .com")]                // space in domain
    [InlineData("a@-example.com")]                  // domain label starts with hyphen
    [InlineData("a@example-.com")]                  // domain label ends with hyphen
    [InlineData("user@127.0.0.1")]                  // IP host
    [InlineData("user@[127.0.0.1]")]                // bracketed IP host
    [InlineData("Name <user@example.com>")]         // display name wrapper
    [InlineData("")]                                // empty
    [InlineData("   ")]                             // whitespace
    [InlineData("o3.test@sdlms")]                   // dotless single label domain
    [InlineData("a@" + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" + ".test")] // domain label > 63 chars
    public async Task CreateUserAsync_WithInvalidEmails_ThrowsBadRequestExceptionWithEmailField(string invalidEmail)
    {
        await using var db = CreateInMemoryDbContext();
        var emailSender = new FakeEmailSender();
        var service = CreateService(db, emailSender);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateUserAsync(_adminId, CreateRequest(invalidEmail)));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("email"), $"Expected errors dictionary to contain 'email' key for input '{invalidEmail}'.");
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.ResidentProfiles.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Equal(0, emailSender.SentCount);
    }

    [Fact]
    public async Task CreateUserAsync_WithLocalPartExceeding64Characters_ThrowsBadRequestException()
    {
        string localPartTooLong = new string('a', 65) + "@example.com";

        await using var db = CreateInMemoryDbContext();
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateUserAsync(_adminId, CreateRequest(localPartTooLong)));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("email"));
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task CreateUserAsync_WithTotalLengthExceeding254Characters_ThrowsBadRequestException()
    {
        // 64 + 1 + 63 + 1 + 63 + 1 + 62 = 255 chars
        string tooLongEmail = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 62);
        Assert.Equal(255, tooLongEmail.Length);

        await using var db = CreateInMemoryDbContext();
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateUserAsync(_adminId, CreateRequest(tooLongEmail)));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("email"));
        Assert.Empty(await db.Users.ToListAsync());
    }
    #endregion

    #region CreateUserAsync - Duplicate Email Conflict
    [Fact]
    public async Task CreateUserAsync_DuplicateEmail_ThrowsConflictExceptionWithAlreadyExists()
    {
        await using var db = CreateInMemoryDbContext();
        var service = CreateService(db);

        await service.CreateUserAsync(_adminId, CreateRequest("existing@example.com"));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateUserAsync(_adminId, CreateRequest("existing@example.com")));

        Assert.Equal(UserErrorCodes.AlreadyExists, ex.ErrorCode);
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateEmailCaseInsensitive_ThrowsConflictExceptionWithAlreadyExists()
    {
        await using var db = CreateInMemoryDbContext();
        var service = CreateService(db);

        await service.CreateUserAsync(_adminId, CreateRequest("admin.user@domain.com"));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateUserAsync(_adminId, CreateRequest("ADMIN.USER@DOMAIN.COM")));

        Assert.Equal(UserErrorCodes.AlreadyExists, ex.ErrorCode);
    }
    #endregion

    #region UpdateUserAsync - Valid and Omitted Email
    [Fact]
    public async Task UpdateUserAsync_WithValidEmail_UpdatesAndNormalizesEmail()
    {
        await using var db = CreateInMemoryDbContext();
        var operatorUser = CreateTestOperator(db, "initial.op@example.com");
        var service = CreateService(db);

        var updateRequest = new UpdateUserRequest("Updated Name", null, "  New.Operator+Ops@Example.Com  ");
        var result = await service.UpdateUserAsync(_adminId, operatorUser.Id, updateRequest);

        Assert.Equal("new.operator+ops@example.com", result.Email);
        var updated = await db.Users.SingleAsync(u => u.Id == operatorUser.Id);
        Assert.Equal("new.operator+ops@example.com", updated.Email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateUserAsync_WithOmittedOrWhitespaceEmail_RetainsExistingEmail(string? omittedEmail)
    {
        await using var db = CreateInMemoryDbContext();
        var operatorUser = CreateTestOperator(db, "keep.this.email@example.com");
        var service = CreateService(db);

        var updateRequest = new UpdateUserRequest("Renamed Operator", null, omittedEmail);
        var result = await service.UpdateUserAsync(_adminId, operatorUser.Id, updateRequest);

        Assert.Equal("keep.this.email@example.com", result.Email);
        var updated = await db.Users.SingleAsync(u => u.Id == operatorUser.Id);
        Assert.Equal("keep.this.email@example.com", updated.Email);
    }

    [Fact]
    public async Task UpdateUserAsync_WithSameEmailAsCurrentUser_SucceedsWithoutConflict()
    {
        await using var db = CreateInMemoryDbContext();
        var operatorUser = CreateTestOperator(db, "same.email@example.com");
        var service = CreateService(db);

        var updateRequest = new UpdateUserRequest("Updated Name", null, "SAME.EMAIL@EXAMPLE.COM");
        var result = await service.UpdateUserAsync(_adminId, operatorUser.Id, updateRequest);

        Assert.Equal("same.email@example.com", result.Email);
    }
    #endregion

    #region UpdateUserAsync - Invalid Email and Conflicts
    [Theory]
    [InlineData("user.@example.com")]
    [InlineData("no-at.domain.com")]
    [InlineData("user@domain.")]
    [InlineData("user@dom_ain.com")]
    [InlineData("user@127.0.0.1")]
    public async Task UpdateUserAsync_WithInvalidEmail_ThrowsBadRequestExceptionAndPreservesExistingEmail(string invalidEmail)
    {
        await using var db = CreateInMemoryDbContext();
        var operatorUser = CreateTestOperator(db, "original@example.com");
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.UpdateUserAsync(_adminId, operatorUser.Id, new UpdateUserRequest("New Name", null, invalidEmail)));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("email"));

        var persisted = await db.Users.SingleAsync(u => u.Id == operatorUser.Id);
        Assert.Equal("original@example.com", persisted.Email);
    }

    [Fact]
    public async Task UpdateUserAsync_WithEmailAlreadyUsedByAnotherUser_ThrowsConflictException()
    {
        await using var db = CreateInMemoryDbContext();
        var user1 = CreateTestOperator(db, "user1@example.com");
        var user2 = CreateTestOperator(db, "user2@example.com");
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateUserAsync(_adminId, user2.Id, new UpdateUserRequest("User 2", null, "user1@example.com")));

        Assert.Equal(UserErrorCodes.AlreadyExists, ex.ErrorCode);
        Assert.Contains("already used by another account", ex.Message);
    }

    [Fact]
    public async Task UpdateUserAsync_WithEmailAlreadyUsedByAnotherUserCaseInsensitive_ThrowsConflictException()
    {
        await using var db = CreateInMemoryDbContext();
        var user1 = CreateTestOperator(db, "unique@domain.vn");
        var user2 = CreateTestOperator(db, "other@domain.vn");
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateUserAsync(_adminId, user2.Id, new UpdateUserRequest("User 2", null, "UNIQUE@DOMAIN.VN")));

        Assert.Equal(UserErrorCodes.AlreadyExists, ex.ErrorCode);
    }
    #endregion

    private static User CreateTestOperator(ApplicationDbContext db, string email)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PhoneNumber = null,
            PasswordHash = "hash123",
            Role = UserRole.LockerOperator,
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ResidentProfile = new ResidentProfile
            {
                Id = Guid.NewGuid(),
                FullName = "Initial Operator Name",
                DeliveryApprovalMode = DeliveryApprovalMode.Manual,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private sealed class FakePasswordHashService : IPasswordHashService
    {
        public string HashPassword(string password) => $"hashed_{password}";
        public bool VerifyPassword(string password, string passwordHash) => passwordHash == $"hashed_{password}";
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        private int _sentCount;
        public int SentCount => _sentCount;

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sentCount);
            return Task.CompletedTask;
        }
    }
}
