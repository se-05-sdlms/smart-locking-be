using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Returns;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Returns;

public sealed class ReturnNoteValidationTests
{
    private static ApplicationDbContext CreateDatabase() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (Guid UserId, Locker Locker, ResidentProfile Resident) SeedResident(
        ApplicationDbContext db,
        UserStatus userStatus = UserStatus.Active,
        LockerOperationalStatus lockerStatus = LockerOperationalStatus.Operational)
    {
        var userId = Guid.NewGuid();
        var lockerId = Guid.NewGuid();

        var locker = new Locker
        {
            Id = lockerId,
            Code = "LOCKER-01",
            DeviceIdentifier = "device-01",
            OperationalStatus = lockerStatus,
            Address = "123 Main St"
        };

        var user = new User
        {
            Id = userId,
            Email = $"resident-{userId:N}@example.test",
            Role = UserRole.Resident,
            Status = userStatus
        };

        var resident = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            User = user,
            FullName = "Nguyen Van A",
            RegisteredLockerId = lockerId,
            RegisteredLocker = locker
        };

        db.AddRange(locker, user, resident);
        db.SaveChanges();

        return (userId, locker, resident);
    }

    private static ReturnRequestService CreateService(ApplicationDbContext db) =>
        new(db,
            lockerAccessService: null!,
            compartmentAllocationService: null!,
            pushNotificationService: null!,
            tokenHashService: new Sha256TokenHashService(),
            timeProvider: TimeProvider.System);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0\u2003")]
    public async Task CreateAsync_MissingOrWhitespaceNote_ThrowsBadRequestAndPersistsNothing(string? note)
    {
        await using var db = CreateDatabase();
        var (userId, _, _) = SeedResident(db);
        var service = CreateService(db);

        var request = new CreateReturnRequest("https://example.test/img.jpg", note);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(userId, request));

        Assert.Equal(CommonErrorCodes.InvalidInput, ex.ErrorCode);
        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("note"));
        Assert.Empty(db.ReturnRequests);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task CreateAsync_ValidNote_IsTrimmedAndPersistedWithAuditLog()
    {
        await using var db = CreateDatabase();
        var (userId, locker, resident) = SeedResident(db);
        var service = CreateService(db);

        const string rawNote = "  Hộp sách\nGiữ nguyên tem  ";
        const string expectedTrimmed = "Hộp sách\nGiữ nguyên tem";
        const string rawImageUrl = "  https://example.test/return-item.jpg  ";
        const string expectedImageUrl = "https://example.test/return-item.jpg";

        var response = await service.CreateAsync(userId, new CreateReturnRequest(rawImageUrl, rawNote));

        Assert.NotNull(response);
        Assert.Equal(expectedTrimmed, response.Note);
        Assert.Equal(expectedImageUrl, response.ImageUrl);
        Assert.Equal(ReturnRequestStatus.Created, response.Status);
        Assert.Equal(locker.Code, response.LockerCode);
        Assert.Equal(string.Empty, response.PickupCode); // Hidden before deposit

        // Check database persistence
        var stored = await db.ReturnRequests.SingleAsync(r => r.Id == response.Id);
        Assert.Equal(resident.Id, stored.ResidentProfileId);
        Assert.Equal(locker.Id, stored.LockerId);
        Assert.Equal(expectedTrimmed, stored.ReturnReason);
        Assert.Equal(expectedImageUrl, stored.ReturnImageUrl);
        Assert.Matches("^[0-9]{6}$", stored.ReturnCode);

        // Check audit log
        var audit = Assert.Single(db.AuditLogs);
        Assert.Equal("ReturnRequest.Created", audit.Action);
        Assert.Equal(userId, audit.ActorUserId);
        Assert.Equal(stored.Id, audit.EntityId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_MissingOrWhitespaceImageUrl_ThrowsBadRequest(string? imageUrl)
    {
        await using var db = CreateDatabase();
        var (userId, _, _) = SeedResident(db);
        var service = CreateService(db);

        var request = new CreateReturnRequest(imageUrl!, "Return reason");

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(userId, request));

        Assert.Equal(CommonErrorCodes.InvalidInput, ex.ErrorCode);
        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("imageUrl"));
        Assert.Empty(db.ReturnRequests);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task CreateAsync_ResidentNotFound_ThrowsNotFoundException()
    {
        await using var db = CreateDatabase();
        var service = CreateService(db);

        var nonExistentUserId = Guid.NewGuid();
        var request = new CreateReturnRequest("https://example.test/img.jpg", "Note");

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(nonExistentUserId, request));

        Assert.Equal(UserErrorCodes.NotFound, ex.ErrorCode);
        Assert.Empty(db.ReturnRequests);
    }

    [Fact]
    public async Task CreateAsync_InactiveResident_ThrowsConflictException()
    {
        await using var db = CreateDatabase();
        var (userId, _, _) = SeedResident(db, userStatus: UserStatus.Disabled);
        var service = CreateService(db);

        var request = new CreateReturnRequest("https://example.test/img.jpg", "Note");

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(userId, request));

        Assert.Equal(ReturnErrorCodes.InvalidState, ex.ErrorCode);
        Assert.Empty(db.ReturnRequests);
    }

    [Fact]
    public async Task CreateAsync_LockerNotOperational_ThrowsConflictException()
    {
        await using var db = CreateDatabase();
        var (userId, _, _) = SeedResident(db, lockerStatus: LockerOperationalStatus.OutOfService);
        var service = CreateService(db);

        var request = new CreateReturnRequest("https://example.test/img.jpg", "Note");

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(userId, request));

        Assert.Equal(LockerErrorCodes.NotOperational, ex.ErrorCode);
        Assert.Empty(db.ReturnRequests);
    }
}
