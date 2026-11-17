using Microsoft.EntityFrameworkCore;
using Npgsql;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using smart_locking_be.Tests.Auth;

namespace smart_locking_be.Tests.Users;

public sealed class UserEmailValidationPostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_user_email_test_{Guid.NewGuid():N}";
    private readonly Guid adminId = Guid.NewGuid();
    private readonly Guid operatorId = Guid.NewGuid();
    private readonly Guid lockerId = Guid.NewGuid();
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
        db.AddRange(
            new User { Id = adminId, Email = "admin@example.test", Role = UserRole.Administrator, Status = UserStatus.Active },
            new User { Id = operatorId, Email = "operator@example.test", Role = UserRole.LockerOperator, Status = UserStatus.Active },
            new Locker { Id = lockerId, Code = "ASSIGNMENT", DeviceIdentifier = "assignment-device", OperationalStatus = LockerOperationalStatus.Operational });
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

    private static readonly string[] InvalidEmails = ["o3.test@sdlms", "", "   ", "no-at.example.test", "@example.test", "a@@example.test", "a.@example.test", "a b@example.test", "Name <a@example.test>", "a@example..test", "a@.example.test", "a@example.test.", "a@example-.test", "a@-example.test", "a@127.0.0.1", new string('a', 65) + "@example.test", "a@" + new string('b', 64) + ".test", new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 62)];

    [PostgresFact]
    public async Task InvalidCreateEmails_RejectBeforeAccountProfileAssignmentAuditOrEmail()
    {
        var email = new RecordingEmail();
        foreach (string value in InvalidEmails)
        {
            await using var db = Context();
            var error = await Record.ExceptionAsync(() => Service(db, email).CreateUserAsync(adminId, Request(value, lockerId)));
            Assert.True(error is BadRequestException, $"Expected email validation for '{value}', received {error?.GetType().Name ?? "success"}.");
            Assert.True(((BadRequestException)error!).Errors?.ContainsKey("email") == true);
        }
        await using var read = Context();
        Assert.Equal(2, await read.Users.CountAsync());
        Assert.Empty(await read.ResidentProfiles.ToListAsync());
        Assert.Empty(await read.OperatorAssignments.ToListAsync());
        Assert.Empty(await read.AuditLogs.ToListAsync());
        Assert.Equal(0, email.Count);
    }

    [PostgresFact]
    public async Task ValidCreate_NormalizesAddressAndPreservesAliasesAndDuplicateConflict()
    {
        var email = new RecordingEmail();
        foreach (string value in new[] { "  First.Last+Ops@Example.Test  ", "operator@sub.example.test", "operator@xn--bcher-kva.example", new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 61) })
        {
            await using var db = Context();
            var created = await Service(db, email).CreateUserAsync(adminId, Request(value, null));
            Assert.Equal(value.Trim().ToLowerInvariant(), created.Email);
        }
        await using (var db = Context())
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() => Service(db, email).CreateUserAsync(adminId, Request("FIRST.LAST+OPS@EXAMPLE.TEST", null)));
            Assert.Equal(UserErrorCodes.AlreadyExists, error.ErrorCode);
        }
        await using var read = Context();
        Assert.Equal(6, await read.Users.CountAsync());
        Assert.Equal(4, await read.ResidentProfiles.CountAsync());
        Assert.Equal(4, await read.AuditLogs.CountAsync(a => a.Action == "User.Created"));
        Assert.Equal(4, email.Count);
    }

    [PostgresFact]
    public async Task InvalidUpdateEmail_PreservesAccountProfileAndAudit()
    {
        foreach (string value in InvalidEmails.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            await using var db = Context();
            var error = await Record.ExceptionAsync(() => Service(db).UpdateUserAsync(adminId, operatorId, new("Changed", null, value)));
            Assert.True(error is BadRequestException, $"Expected email validation for '{value}', received {error?.GetType().Name ?? "success"}.");
            Assert.True(((BadRequestException)error!).Errors?.ContainsKey("email") == true);
        }
        await using var read = Context();
        Assert.Equal("operator@example.test", (await read.Users.SingleAsync(u => u.Id == operatorId)).Email);
        Assert.Empty(await read.ResidentProfiles.ToListAsync());
        Assert.Empty(await read.AuditLogs.ToListAsync());
    }

    [PostgresFact]
    public async Task ValidUpdateAndOmittedEmail_KeepExistingPartialUpdateSemantics()
    {
        await using (var db = Context())
        {
            var result = await Service(db).UpdateUserAsync(adminId, operatorId, new("Operator", null, "  Updated+Ops@Sub.Example.Test  "));
            Assert.Equal("updated+ops@sub.example.test", result.Email);
        }
        await using (var db = Context())
        {
            var result = await Service(db).UpdateUserAsync(adminId, operatorId, new("Renamed", null, null));
            Assert.Equal("updated+ops@sub.example.test", result.Email);
        }
        await using var read = Context();
        Assert.Equal("Renamed", (await read.ResidentProfiles.SingleAsync(p => p.UserId == operatorId)).FullName);
        Assert.Equal(2, await read.AuditLogs.CountAsync(a => a.Action == "User.Updated"));
    }

    private static CreateUserRequest Request(string email, Guid? locker) => new("New operator", email, null, UserRole.LockerOperator, null, locker, "Initial");
    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
    private static UserService Service(ApplicationDbContext db, RecordingEmail? email = null) => new(db, new TestHash(), email ?? new RecordingEmail());
    private sealed class TestHash : IPasswordHashService
    {
        public string HashPassword(string password) => "test-hash";
        public bool VerifyPassword(string password, string passwordHash) => false;
    }
    private sealed class RecordingEmail : IEmailSender
    {
        private int count;
        public int Count => count;
        public bool Fail { get; init; }
        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
        {
            if (Fail) throw new ServiceUnavailableException("email/unavailable", "Test sender unavailable");
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        }
    }
}
