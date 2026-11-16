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

public sealed class ReturnNotePostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_return_note_test_{Guid.NewGuid():N}";
    private readonly Guid userId = Guid.NewGuid(), lockerId = Guid.NewGuid();
    private string connectionString = string.Empty, adminConnectionString = string.Empty;
    private const string ImageUrl = "https://example.test/return.jpg";

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
        var locker = new Locker { Id = lockerId, Code = "RETURN-NOTE", DeviceIdentifier = "return-note", OperationalStatus = LockerOperationalStatus.Operational };
        db.AddRange(locker, new ResidentProfile
        {
            Id = Guid.NewGuid(),
            FullName = "Test resident",
            RegisteredLocker = locker,
            User = new User { Id = userId, Email = "return-note@example.test", Role = UserRole.Resident, Status = UserStatus.Active }
        });
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
    public async Task MissingOrWhitespaceNote_RejectsWithoutCreatingReturnAuditOrReservation()
    {
        foreach (string? note in new string?[] { null, "", "  ", "\t\r\n", "\u00a0\u2003" })
        {
            await using var db = Context();
            var error = await Assert.ThrowsAsync<BadRequestException>(() => Service(db).CreateAsync(userId, new(ImageUrl, note)));
            Assert.Equal(CommonErrorCodes.InvalidInput, error.ErrorCode);
            Assert.True(error.Errors?.ContainsKey("note") == true);
            Assert.Empty(db.ChangeTracker.Entries<ReturnRequest>());
        }
        await using var read = Context();
        Assert.Empty(await read.ReturnRequests.ToListAsync());
        Assert.Empty(await read.AuditLogs.ToListAsync());
        Assert.Empty(await read.CompartmentReservations.ToListAsync());
    }

    [PickupPostgresFact]
    public async Task ValidNote_IsTrimmedAndRoundTripsThroughCreateDetailAndList()
    {
        await using var db = Context();
        var service = Service(db);
        var created = await service.CreateAsync(userId, new(ImageUrl, "  Hộp sách\nGiữ nguyên tem  "));
        Assert.Equal("Hộp sách\nGiữ nguyên tem", created.Note);
        Assert.Equal(ImageUrl, created.ImageUrl);
        Assert.Equal(ReturnRequestStatus.Created, created.Status);
        Assert.Equal(string.Empty, created.PickupCode); // Pickup credentials stay hidden until deposit.
        Assert.Null(created.CompartmentCode);
        Assert.Equal(created, await service.GetAsync(userId, created.Id));
        Assert.Equal(created, Assert.Single((await service.GetMineAsync(userId)).Items));
        await using var read = Context();
        var stored = await read.ReturnRequests.SingleAsync();
        Assert.Equal(created.Note, stored.ReturnReason);
        Assert.Matches("^[0-9]{6}$", stored.ReturnCode);
        Assert.Single(await read.AuditLogs.Where(a => a.Action == "ReturnRequest.Created").ToListAsync());
        Assert.Empty(await read.CompartmentReservations.ToListAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => Service(read).GetAsync(Guid.NewGuid(), created.Id));
    }

    [PickupPostgresFact]
    public async Task ValidNote_DoesNotBypassImageValidation()
    {
        await using var db = Context();
        var error = await Assert.ThrowsAsync<BadRequestException>(() => Service(db).CreateAsync(userId, new(" ", "Hộp sách")));
        Assert.True(error.Errors?.ContainsKey("imageUrl") == true);
        Assert.Empty(await db.ReturnRequests.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
    // Creating metadata must never call hardware allocation, opening or push providers.
    private static ReturnRequestService Service(ApplicationDbContext db) => new(db, null!, null!, null!, new Sha256TokenHashService(), TimeProvider.System);
}
