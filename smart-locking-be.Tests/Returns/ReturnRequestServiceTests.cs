using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Returns;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Returns;

public sealed class ReturnRequestServiceTests
{
    #region RET-01 to RET-06 Matrix Tests

    [Fact]
    [Trait("Category", "RET-01")]
    public async Task CreateAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        ReturnRequestResponse res = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", "Reason"));

        Assert.Equal(ReturnRequestStatus.Created, res.Status);
        Assert.NotEqual(Guid.Empty, res.Id);
    }

    [Fact]
    [Trait("Category", "RET-01")]
    public async Task CreateAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(Guid.NewGuid(), new CreateReturnRequest("https://example.com/return.jpg", null)));
    }

    [Fact]
    [Trait("Category", "RET-01")]
    public async Task CreateAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        locker.OperationalStatus = LockerOperationalStatus.OutOfService;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null)));
    }

    [Fact]
    [Trait("Category", "RET-01")]
    public async Task CreateAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(userId, new CreateReturnRequest("", null)));
    }

    [Fact]
    [Trait("Category", "RET-01")]
    public async Task CreateAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, ResidentProfile resident, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        resident.RegisteredLockerId = Guid.Empty;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(userId, new CreateReturnRequest("https://example.com/item.jpg", null)));
    }

    [Fact]
    [Trait("Category", "RET-01")]
    public async Task CreateAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.Status = UserStatus.Disabled;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(userId, new CreateReturnRequest("https://example.com/item.jpg", null)));
    }

    [Fact]
    [Trait("Category", "RET-02")]
    public async Task GetMineAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));

        var result = await service.GetMineAsync(userId, default, 1, 10);

        Assert.Single(result.Items);
    }

    [Fact]
    [Trait("Category", "RET-02")]
    public async Task GetMineAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        var result = await service.GetMineAsync(userId, default, 1, 10);

        Assert.Empty(result.Items);
    }

    [Fact]
    [Trait("Category", "RET-02")]
    public async Task GetMineAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        var result = await service.GetMineAsync(Guid.NewGuid(), default, 1, 10);
        Assert.Empty(result.Items);
    }

    [Fact]
    [Trait("Category", "RET-03")]
    public async Task GetAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));

        var res = await service.GetAsync(userId, created.Id);

        Assert.Equal(created.Id, res.Id);
    }

    [Fact]
    [Trait("Category", "RET-03")]
    public async Task GetAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(userId, Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "RET-03")]
    public async Task GetAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        Guid otherUser = Guid.NewGuid();
        db.Users.Add(new User { Id = otherUser, PhoneNumber = "0988776655", PasswordHash = "x", Role = UserRole.Resident, Status = UserStatus.Active });
        db.ResidentProfiles.Add(new ResidentProfile { Id = Guid.NewGuid(), UserId = otherUser, FullName = "Other" });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(otherUser, created.Id));
    }

    [Fact]
    [Trait("Category", "RET-03")]
    public async Task GetAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "RET-03")]
    public async Task GetAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(userId, Guid.Empty));
    }

    [Fact]
    [Trait("Category", "RET-03")]
    public async Task GetAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "RET-04")]
    public async Task OpenCompartmentAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));

        var unlock = await service.OpenCompartmentAsync(userId, created.Id);

        Assert.NotNull(unlock.CompartmentCode);
    }

    [Fact]
    [Trait("Category", "RET-04")]
    public async Task OpenCompartmentAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.OpenCompartmentAsync(userId, Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "RET-04")]
    public async Task OpenCompartmentAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        Guid otherUser = Guid.NewGuid();
        db.Users.Add(new User { Id = otherUser, PhoneNumber = "0988776655", PasswordHash = "x", Role = UserRole.Resident, Status = UserStatus.Active });
        db.ResidentProfiles.Add(new ResidentProfile { Id = Guid.NewGuid(), UserId = otherUser, FullName = "Other" });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.OpenCompartmentAsync(otherUser, created.Id));
    }

    [Fact]
    [Trait("Category", "RET-04")]
    public async Task OpenCompartmentAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id); // Already allocated

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(userId, created.Id));
    }

    [Fact]
    [Trait("Category", "RET-04")]
    public async Task OpenCompartmentAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        // Deactivate compartments to simulate no compartment available
        foreach (var c in db.LockerCompartments) { c.OperationalStatus = LockerCompartmentOperationalStatus.OutOfService; }
        await db.SaveChangesAsync();
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(userId, created.Id));
    }

    [Fact]
    [Trait("Category", "RET-04")]
    public async Task OpenCompartmentAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.OpenCompartmentAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "RET-04")]
    public async Task OpenCompartmentAsync_UTC07()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, _) = await SeedReturnSetupAsync(db);
        (_, _, _, ReturnRequestService failingService) = await SeedReturnSetupAsync(db, new FailingAccess("Device offline"));
        var created = await failingService.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => failingService.OpenCompartmentAsync(userId, created.Id));
        Assert.Equal("Device offline", ex.Message);
    }

    [Fact]
    [Trait("Category", "RET-04")]
    public async Task OpenCompartmentAsync_UTC08()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, _) = await SeedReturnSetupAsync(db);
        (_, _, _, ReturnRequestService failingService) = await SeedReturnSetupAsync(db, new FailingAccess(null));
        var created = await failingService.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => failingService.OpenCompartmentAsync(userId, created.Id));
        Assert.Equal("Không thể mở ngăn tủ.", ex.Message);
    }

    #endregion

    #region RET-05: FinalizeDepositAsync
    [Fact]
    [Trait("Category", "RET-05")]
    public async Task FinalizeDepositAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);

        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);

        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);
        Assert.Equal(ReturnRequestStatus.Deposited, deposited.Status);
        Assert.NotNull(deposited.ReturnCode);
    }

    [Fact]
    [Trait("Category", "RET-05")]
    public async Task FinalizeDepositAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.FinalizeDepositAsync(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    [Trait("Category", "RET-05")]
    public async Task FinalizeDepositAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));

        // Created status, not allocated yet
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow));
    }

    [Fact]
    [Trait("Category", "RET-05")]
    public async Task FinalizeDepositAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        var req = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);
        req.AllocatedCompartmentId = null;
        req.AllocatedCompartment = null;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow));
    }

    [Fact]
    [Trait("Category", "RET-05")]
    public async Task FinalizeDepositAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);

        // Call again when status is already Deposited (idempotent)
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);

        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);
        Assert.Equal(ReturnRequestStatus.Deposited, deposited.Status);
    }

    [Fact]
    [Trait("Category", "RET-06")]
    public async Task ExpireReservationsAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        var req = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);
        req.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();

        int expired = await service.ExpireReservationsAsync();

        Assert.Equal(1, expired);
    }

    [Fact]
    [Trait("Category", "RET-06")]
    public async Task ExpireReservationsAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);

        int expired = await service.ExpireReservationsAsync();

        Assert.Equal(0, expired);
    }

    [Fact]
    [Trait("Category", "RET-06")]
    public async Task ExpireReservationsAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        int expired = await service.ExpireReservationsAsync();

        Assert.Equal(0, expired);
    }

    #endregion

    #region RPU-01 to RPU-04 Matrix Tests

    [Fact]
    [Trait("Category", "RPU-01")]
    public async Task ReturnPickup_CreateAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode));

        Assert.NotNull(session.GuestSessionToken);
        Assert.Equal(deposited.Id, session.ReturnRequestId);
    }

    [Fact]
    [Trait("Category", "RPU-01")]
    public async Task ReturnPickup_CreateAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        IReturnPickupSessionService pickup = service;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, "999999")));
    }

    [Fact]
    [Trait("Category", "RPU-01")]
    public async Task ReturnPickup_CreateAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => pickup.CreateAsync(new ValidateReturnPickupRequest("WRONG_LOCKER", deposited.ReturnCode)));
    }

    [Fact]
    [Trait("Category", "RPU-01")]
    public async Task ReturnPickup_CreateAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        var req = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);
        req.ReturnCode = "123456"; // not deposited yet
        await db.SaveChangesAsync();

        IReturnPickupSessionService pickup = service;
        await Assert.ThrowsAsync<InvalidOperationException>(() => pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, "123456")));
    }

    [Fact]
    [Trait("Category", "RPU-01")]
    public async Task ReturnPickup_CreateAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);
        deposited.Status = ReturnRequestStatus.Completed; // already picked up
        await db.SaveChangesAsync();

        IReturnPickupSessionService pickup = service;
        await Assert.ThrowsAsync<InvalidOperationException>(() => pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode)));
    }

    [Fact]
    [Trait("Category", "RPU-01")]
    public async Task ReturnPickup_CreateAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        IReturnPickupSessionService pickup = service;
        await Assert.ThrowsAsync<ArgumentException>(() => pickup.CreateAsync(new ValidateReturnPickupRequest("", "")));
    }

    [Fact]
    [Trait("Category", "RPU-02")]
    public async Task ReturnPickup_GetAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode));

        var getRes = await pickup.GetAsync(session.ReturnRequestId, session.GuestSessionToken);
        Assert.Equal(session.ReturnRequestId, getRes.ReturnRequestId);
    }

    [Fact]
    [Trait("Category", "RPU-02")]
    public async Task ReturnPickup_GetAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        IReturnPickupSessionService pickup = service;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => pickup.GetAsync(Guid.NewGuid(), "TOKEN"));
    }

    [Fact]
    [Trait("Category", "RPU-02")]
    public async Task ReturnPickup_GetAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode));

        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => pickup.GetAsync(session.ReturnRequestId, "WRONG_TOKEN"));
    }

    [Fact]
    [Trait("Category", "RPU-02")]
    public async Task ReturnPickup_GetAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode));

        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => pickup.GetAsync(session.ReturnRequestId, ""));
    }

    [Fact]
    [Trait("Category", "RPU-02")]
    public async Task ReturnPickup_GetAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode));
        var req = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);
        req.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => pickup.GetAsync(session.ReturnRequestId, session.GuestSessionToken));
    }

    [Fact]
    [Trait("Category", "RPU-02")]
    public async Task ReturnPickup_GetAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        IReturnPickupSessionService pickup = service;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => pickup.GetAsync(Guid.Empty, "TOKEN"));
    }

    [Fact]
    [Trait("Category", "RPU-03")]
    public async Task ReturnPickup_OpenCompartmentAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode));

        var openRes = await pickup.OpenCompartmentAsync(session.ReturnRequestId, session.GuestSessionToken);
        Assert.NotNull(openRes.CompartmentCode);
    }

    [Fact]
    [Trait("Category", "RPU-03")]
    public async Task ReturnPickup_OpenCompartmentAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode));

        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => pickup.OpenCompartmentAsync(session.ReturnRequestId, "WRONG"));
    }

    [Fact]
    [Trait("Category", "RPU-03")]
    public async Task ReturnPickup_OpenCompartmentAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode));
        deposited.Status = ReturnRequestStatus.Completed; // Not deposited
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => pickup.OpenCompartmentAsync(session.ReturnRequestId, session.GuestSessionToken));
    }

    [Fact]
    [Trait("Category", "RPU-03")]
    public async Task ReturnPickup_OpenCompartmentAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        IReturnPickupSessionService pickup = service;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => pickup.OpenCompartmentAsync(Guid.NewGuid(), "TOKEN"));
    }

    [Fact]
    [Trait("Category", "RPU-03")]
    public async Task ReturnPickup_OpenCompartmentAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode));
        deposited.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => pickup.OpenCompartmentAsync(session.ReturnRequestId, session.GuestSessionToken));
    }

    [Fact]
    [Trait("Category", "RPU-03")]
    public async Task ReturnPickup_OpenCompartmentAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode));
        deposited.AllocatedCompartmentId = null;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => pickup.OpenCompartmentAsync(session.ReturnRequestId, session.GuestSessionToken));
    }

    [Fact]
    [Trait("Category", "RPU-03")]
    public async Task ReturnPickup_OpenCompartmentAsync_UTC07()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, _) = await SeedReturnSetupAsync(db);
        (_, _, _, ReturnRequestService failingService) = await SeedReturnSetupAsync(db, new FailingAccess("Device offline"));
        var created = await failingService.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        var req = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);
        req.Status = ReturnRequestStatus.Deposited;
        req.AllocatedCompartmentId = db.LockerCompartments.First().Id;
        req.ReturnCode = "123456";
        await db.SaveChangesAsync();

        IReturnPickupSessionService pickup = failingService;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, "123456"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pickup.OpenCompartmentAsync(session.ReturnRequestId, session.GuestSessionToken));
        Assert.Equal("Device offline", ex.Message);
    }

    [Fact]
    [Trait("Category", "RPU-03")]
    public async Task ReturnPickup_OpenCompartmentAsync_UTC08()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, Locker locker, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);
        var deposited = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);

        IReturnPickupSessionService pickup = service;
        var session = await pickup.CreateAsync(new ValidateReturnPickupRequest(locker.Code, deposited.ReturnCode));
        deposited.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => pickup.OpenCompartmentAsync(session.ReturnRequestId, session.GuestSessionToken));
    }

    #endregion

    #region RPU-04: FinalizePickupAsync
    [Fact]
    [Trait("Category", "RPU-04")]
    public async Task ReturnPickup_FinalizePickupAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);

        IReturnPickupSessionService pickup = service;
        await pickup.FinalizePickupAsync(created.Id, DateTimeOffset.UtcNow);

        var finalized = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);
        Assert.Equal(ReturnRequestStatus.Completed, finalized.Status);
    }

    [Fact]
    [Trait("Category", "RPU-04")]
    public async Task ReturnPickup_FinalizePickupAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDb();
        (_, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);

        IReturnPickupSessionService pickup = service;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => pickup.FinalizePickupAsync(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    [Trait("Category", "RPU-04")]
    public async Task ReturnPickup_FinalizePickupAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, DateTimeOffset.UtcNow);

        IReturnPickupSessionService pickup = service;
        await pickup.FinalizePickupAsync(created.Id, DateTimeOffset.UtcNow);

        // Idempotent call
        await pickup.FinalizePickupAsync(created.Id, DateTimeOffset.UtcNow);
        var finalized = await db.ReturnRequests.SingleAsync(r => r.Id == created.Id);
        Assert.Equal(ReturnRequestStatus.Completed, finalized.Status);
    }

    [Fact]
    [Trait("Category", "RPU-04")]
    public async Task ReturnPickup_FinalizePickupAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDb();
        (Guid userId, _, _, ReturnRequestService service) = await SeedReturnSetupAsync(db);
        var created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));

        // Created status, not Deposited yet
        IReturnPickupSessionService pickup = service;
        await Assert.ThrowsAsync<InvalidOperationException>(() => pickup.FinalizePickupAsync(created.Id, DateTimeOffset.UtcNow));
    }

    #endregion

    private static async Task<(Guid UserId, Locker Locker, ResidentProfile Resident, ReturnRequestService Service)> SeedReturnSetupAsync(ApplicationDbContext db, ILockerAccessService? accessService = null)
    {
        Guid userId = Guid.NewGuid(); Guid lockerId = Guid.NewGuid(); Guid compartmentId = Guid.NewGuid(); DateTimeOffset now = DateTimeOffset.UtcNow;
        User user = new() { Id = userId, PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}", PasswordHash = "x", Role = UserRole.Resident, Status = UserStatus.Active, CreatedAt = now, UpdatedAt = now };
        Locker locker = new() { Id = lockerId, Code = $"LK-{Random.Shared.Next(10, 99)}", Address = "A", RecoveryAddress = "B", DeviceIdentifier = "SIM", OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online, CreatedAt = now, UpdatedAt = now };
        ResidentProfile resident = new() { Id = Guid.NewGuid(), UserId = userId, User = user, RegisteredLockerId = lockerId, RegisteredLocker = locker, FullName = "Test", DeliveryApprovalMode = DeliveryApprovalMode.Manual, CreatedAt = now, UpdatedAt = now };
        LockerCompartment compartment = new() { Id = compartmentId, LockerId = lockerId, Locker = locker, Code = "A01", HardwareCode = "A01", HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed, CreatedAt = now, UpdatedAt = now };
        SystemPolicy policy = new() { Id = Guid.NewGuid(), Version = 1, IsActive = true, GuestSessionTimeoutMinutes = 10, CompartmentReservationMinutes = 10, Currency = "VND", EffectiveFrom = now, CreatedAt = now, CreatedByUserId = Guid.NewGuid() };
        db.AddRange(user, locker, resident, compartment, policy);
        await db.SaveChangesAsync();

        var service = new ReturnRequestService(
            db,
            accessService ?? new SuccessfulAccess(),
            new CompartmentAllocationService(db, TimeProvider.System),
            new NoopPush(),
            new Sha256TokenHashService(),
            TimeProvider.System);

        return (userId, locker, resident, service);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class SuccessfulAccess : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new OpenLockerResponse(Guid.NewGuid(), request.LockerId, request.LockerCompartmentId, "SIM", 1, LockerAccessResult.Succeeded, null, DateTimeOffset.UtcNow));
    }
    private sealed class FailingAccess(string? reason) : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new OpenLockerResponse(Guid.NewGuid(), request.LockerId, request.LockerCompartmentId, "SIM", 1, LockerAccessResult.Failed, reason, DateTimeOffset.UtcNow));
    }
    private sealed class NoopPush : IPushNotificationService
    {
        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode) => Guid.NewGuid();
        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode) => Guid.NewGuid();
        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => Guid.NewGuid();
        public Guid EnqueueParcelTransferred(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string collectionAddress) => Guid.NewGuid();
        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue) => Guid.NewGuid();
        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
