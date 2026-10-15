using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Parcels;

public sealed class ParcelServiceTests
{
    [Fact]
    [Trait("Category", "PAR-01")]
    public async Task GetParcelsAsync_UTC01()
    {
        await ListCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "PAR-01")]
    public async Task GetParcelsAsync_UTC02()
    {
        await ListCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "PAR-01")]
    public async Task GetParcelsAsync_UTC03()
    {
        await ListCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "PAR-01")]
    public async Task GetParcelsAsync_UTC04()
    {
        await ListCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "PAR-02")]
    public async Task GetParcelAsync_UTC01()
    {
        await DetailCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "PAR-02")]
    public async Task GetParcelAsync_UTC02()
    {
        await DetailCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "PAR-02")]
    public async Task GetParcelAsync_UTC03()
    {
        await DetailCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "PAR-03")]
    public async Task GetHistoryAsync_UTC01()
    {
        await HistoryCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "PAR-03")]
    public async Task GetHistoryAsync_UTC02()
    {
        await HistoryCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "PAR-03")]
    public async Task GetHistoryAsync_UTC03()
    {
        await HistoryCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC01()
    {
        await OpenCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC02()
    {
        await OpenCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC03()
    {
        await OpenCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC04()
    {
        await OpenCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC05()
    {
        await OpenCaseAsync(5);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC06()
    {
        await OpenCaseAsync(6);
    }

    [Fact]
    [Trait("Category", "PAR-05")]
    public async Task FinalizeRetrievalAsync_UTC01()
    {
        await FinalizeCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "PAR-05")]
    public async Task FinalizeRetrievalAsync_UTC02()
    {
        await FinalizeCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "PAR-05")]
    public async Task FinalizeRetrievalAsync_UTC03()
    {
        await FinalizeCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "PAR-05")]
    public async Task FinalizeRetrievalAsync_UTC04()
    {
        await FinalizeCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC01()
    {
        await TransferCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC02()
    {
        await TransferCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC03()
    {
        await TransferCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC04()
    {
        await TransferCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC05()
    {
        await TransferCaseAsync(5);
    }


    private static async Task ListCaseAsync(int utc)
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service;
        if (utc == 2)
            await Error<ArgumentException>(() => service.GetParcelsAsync(fixture.User.Id, "Resident", ParcelListView.Active, "", Now, Now.AddDays(-1), default), "From date must not be after to date.");
        else if (utc == 3)
            await Error<ArgumentException>(() => service.GetParcelsAsync(fixture.User.Id, "Resident", (ParcelListView)999, "", null, null, default), "Parcel view is invalid.");
        else if (utc == 4)
        {
            // ParcelListView cannot be null; default is its representable boundary value.
            await Error<UnauthorizedAccessException>(() => service.GetParcelsAsync(Guid.Empty, "", default, "", null, null, default), "Role cannot access parcels.");
        }
        else
        {
            var result = await service.GetParcelsAsync(fixture.User.Id, "Resident", ParcelListView.Active, "P1", Now.AddDays(-2), Now, default);
            var item = Assert.Single(result);
            Assert.Equal(fixture.Parcel.Id, item.Id);
            Assert.Equal(fixture.Compartment.Id, item.CompartmentId);
            Assert.Equal("L01", item.LockerCode);
            Assert.Equal(ParcelStatus.Stored, item.Status);
            // The matrix requires pagination; the current service returns a collection.
            Assert.IsType<PagedResult<ParcelListItemResponse>>(result);
        }
        fixture.AssertNoCalls();
        Assert.Empty(fixture.Db.AuditLogs);
    }
    private static async Task DetailCaseAsync(int utc)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (utc == 1)
        {
            var result = await fixture.Service.GetParcelAsync(fixture.User.Id, "Resident", fixture.Parcel.Id, default);
            Assert.Equal(fixture.Parcel.Id, result.Id);
            Assert.Equal("P1", result.ParcelCode);
            Assert.Equal(fixture.Compartment.Id, result.CompartmentId);
            Assert.Equal("C01", result.CompartmentCode);
            Assert.Equal("Recovery", result.LockerRecoveryAddress);
        }
        else if (utc == 2)
            await Error<KeyNotFoundException>(() => fixture.Service.GetParcelAsync(fixture.User.Id, "Resident", Guid.NewGuid(), default), "Parcel not found.");
        else
            await Error<UnauthorizedAccessException>(() => fixture.Service.GetParcelAsync(Guid.Empty, "", Guid.Empty, default), "Role cannot access parcels.");
        fixture.AssertNoCalls();
        Assert.Empty(fixture.Db.AuditLogs);
    }
    private static async Task HistoryCaseAsync(int utc)
    {
        await using var fixture = await Fixture.CreateAsync();
        var older = new ParcelStatusHistory { Id = Guid.NewGuid(), ParcelId = fixture.Parcel.Id, ToStatus = ParcelStatus.Stored, ChangedAt = Now.AddDays(-1) };
        var newer = new ParcelStatusHistory { Id = Guid.NewGuid(), ParcelId = fixture.Parcel.Id, FromStatus = ParcelStatus.Stored, ToStatus = ParcelStatus.Overdue, ChangedAt = Now };
        fixture.Db.AddRange(older, newer, new ParcelStatusHistory { Id = Guid.NewGuid(), ParcelId = Guid.NewGuid(), ToStatus = ParcelStatus.Stored, ChangedAt = Now });
        await fixture.Db.SaveChangesAsync();
        if (utc == 1)
        {
            var result = await fixture.Service.GetHistoryAsync(fixture.User.Id, "Resident", fixture.Parcel.Id, default);
            Assert.Equal(new[] { newer.Id, older.Id }, result.Select(h => h.Id));
            Assert.IsType<PagedResult<ParcelStatusHistoryResponse>>(result);
        }
        else if (utc == 2)
            await Error<KeyNotFoundException>(() => fixture.Service.GetHistoryAsync(fixture.User.Id, "Resident", Guid.NewGuid(), default), "Parcel not found.");
        else
            await Error<UnauthorizedAccessException>(() => fixture.Service.GetHistoryAsync(Guid.Empty, "", Guid.Empty, default), "Role cannot access parcels.");
        fixture.AssertNoCalls();
        Assert.Empty(fixture.Db.AuditLogs);
    }
    private static async Task OpenCaseAsync(int utc)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (utc == 4)
            fixture.Parcel.Status = ParcelStatus.Removed;
        if (utc == 5)
            fixture.Db.OverdueCharges.Add(new OverdueCharge { Id = Guid.NewGuid(), ParcelId = fixture.Parcel.Id, Currency = "VND", Amount = 5000, Status = OverdueChargeStatus.Outstanding });
        await fixture.Db.SaveChangesAsync();
        ParcelStatus before = fixture.Parcel.Status;
        // PAR-04's implementation is ParcelPickupService.UnlockAsync in dev.
        var service = new ParcelPickupService(fixture.Db, fixture.Access, new Clock());
        if (utc == 1)
        {
            var result = await service.UnlockAsync(fixture.User.Id, fixture.Parcel.Id, "127.0.0.1", "Mobile");
            Assert.Equal(fixture.Parcel.Id, result.ParcelId);
            Assert.Equal(LockerAccessResult.Succeeded, result.Result);
            Assert.Equal(Now, result.RequestedAt);
            var call = Assert.Single(fixture.Access.Calls);
            Assert.Equal(fixture.Parcel.Id, call.ParcelId);
            Assert.Equal(fixture.User.Id, call.UserId);
            Assert.Equal(fixture.Compartment.Id, call.LockerCompartmentId);
            Assert.Equal(LockerAccessType.ResidentPickup, call.AccessType);
            Assert.Equal(LockerAccessMethod.RemoteApp, call.AccessMethod);
            Assert.Equal("127.0.0.1", call.IpAddress);
        }
        else
        {
            if (utc == 2 || utc == 6)
                await Error<KeyNotFoundException>(() => service.UnlockAsync(utc == 6 ? Guid.Empty : fixture.User.Id, utc == 6 ? Guid.Empty : Guid.NewGuid(), "", ""), "Không tìm thấy bưu kiện.");
            if (utc == 3)
                await Error<UnauthorizedAccessException>(() => service.UnlockAsync(Guid.NewGuid(), fixture.Parcel.Id, null, null), "Bạn không có quyền mở ngăn chứa bưu kiện này.");
            if (utc == 4)
                await Error<InvalidOperationException>(() => service.UnlockAsync(fixture.User.Id, fixture.Parcel.Id, null, null), "Bưu kiện không còn ở trạng thái có thể nhận.");
            if (utc == 5)
                await Error<InvalidOperationException>(() => service.UnlockAsync(fixture.User.Id, fixture.Parcel.Id, null, null), "Cần thanh toán phí quá hạn trước khi nhận bưu kiện.");
            Assert.Empty(fixture.Access.Calls);
        }
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(before, (await fixture.Db.Parcels.SingleAsync()).Status);
        Assert.Empty(fixture.Db.ParcelStatusHistories);
        Assert.Empty(fixture.Db.AuditLogs);
        Assert.Empty(fixture.Push.Transfers);
        Assert.Empty(fixture.Push.Sent);
    }
    private static async Task FinalizeCaseAsync(int utc)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (utc == 3)
        {
            fixture.Parcel.Status = ParcelStatus.Removed;
            await fixture.Db.SaveChangesAsync();
        }
        ParcelStatus before = fixture.Parcel.Status;
        if (utc == 1)
        {
            await fixture.Service.FinalizeRetrievalAsync(fixture.Parcel.Id, fixture.User.Id, Now);
            fixture.Db.ChangeTracker.Clear();
            var saved = await fixture.Db.Parcels.SingleAsync();
            Assert.Equal(ParcelStatus.Retrieved, saved.Status);
            Assert.Equal(Now, saved.RetrievedAt);
            Assert.Equal(Now, saved.UpdatedAt);
            Assert.Equal(Now, (await fixture.Db.DeliveryRequests.SingleAsync()).CompartmentReleasedAt);
            var history = Assert.Single(fixture.Db.ParcelStatusHistories);
            Assert.Equal(saved.Id, history.ParcelId);
            Assert.Equal(ParcelStatus.Stored, history.FromStatus);
            Assert.Equal(ParcelStatus.Retrieved, history.ToStatus);
            Assert.Equal(fixture.User.Id, history.ChangedByUserId);
            Assert.Equal(Now, history.ChangedAt);
        }
        else
        {
            if (utc == 3)
                await Error<InvalidOperationException>(() => fixture.Service.FinalizeRetrievalAsync(fixture.Parcel.Id, fixture.User.Id, Now), "Bưu kiện không còn ở trạng thái có thể hoàn tất lấy hàng.");
            else
                await Error<KeyNotFoundException>(() => fixture.Service.FinalizeRetrievalAsync(utc == 4 ? Guid.Empty : Guid.NewGuid(), utc == 4 ? Guid.Empty : fixture.User.Id, utc == 4 ? default : Now), "Không tìm thấy bưu kiện.");
            fixture.Db.ChangeTracker.Clear();
            Assert.Equal(before, (await fixture.Db.Parcels.SingleAsync()).Status);
            Assert.Empty(fixture.Db.ParcelStatusHistories);
        }
        fixture.AssertNoCalls();
        Assert.Empty(fixture.Db.AuditLogs);
    }
    private static async Task TransferCaseAsync(int utc)
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid admin = Guid.NewGuid();
        fixture.Parcel.MaxStorageUntil = Now;
        if (utc == 3)
            fixture.Parcel.MaxStorageUntil = Now.AddTicks(1);
        if (utc == 4)
        {
            fixture.Request.ResidentProfileId = null;
            fixture.Request.ResidentProfile = null;
        }
        await fixture.Db.SaveChangesAsync();
        if (utc == 1)
        {
            var result = await fixture.Service.TransferOverdueAsync(admin, "Administrator", fixture.Parcel.Id);
            Assert.Equal(fixture.Parcel.Id, result.ParcelId);
            Assert.Equal("Recovery", result.CollectionAddress);
            Assert.Equal("Removed", result.Status);
            fixture.Db.ChangeTracker.Clear();
            var saved = await fixture.Db.Parcels.SingleAsync();
            Assert.Equal(ParcelStatus.Removed, saved.Status);
            Assert.Equal(Now, saved.RemovedAt);
            Assert.Equal(admin, saved.RemovedByUserId);
            Assert.Equal((fixture.User.Id, fixture.Request.Id, saved.Id, "Recovery"), Assert.Single(fixture.Push.Transfers));
            Assert.Equal(fixture.Push.NotificationId, Assert.Single(fixture.Push.Sent));
            Audit(fixture.Db, "Parcel.Transferred", saved.Id, admin);
        }
        else
        {
            if (utc == 2 || utc == 5)
                await Error<KeyNotFoundException>(() => fixture.Service.TransferOverdueAsync(utc == 5 ? Guid.Empty : admin, utc == 5 ? "" : "Administrator", utc == 5 ? Guid.Empty : Guid.NewGuid()), "Không tìm thấy kiện hàng.");
            if (utc == 3)
                await Error<InvalidOperationException>(() => fixture.Service.TransferOverdueAsync(admin, "Administrator", fixture.Parcel.Id), "Kiện hàng chưa đủ điều kiện chuyển điểm tập kết.");
            if (utc == 4)
                await Error<InvalidOperationException>(() => fixture.Service.TransferOverdueAsync(admin, "Administrator", fixture.Parcel.Id), "Kiện hàng không có cư dân nhận.");
            fixture.Db.ChangeTracker.Clear();
            Assert.Equal(ParcelStatus.Stored, (await fixture.Db.Parcels.SingleAsync()).Status);
            Assert.Empty(fixture.Db.AuditLogs);
            fixture.AssertNoCalls();
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = CreateDb();
        public User User { get; private set; } = null!;
        public LockerCompartment Compartment { get; private set; } = null!;
        public DeliveryRequest Request { get; private set; } = null!;
        public Parcel Parcel { get; private set; } = null!;
        public RecordingAccess Access { get; } = new();
        public RecordingPush Push { get; } = new();
        public ParcelService Service => new(Db, Push, new Clock());
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            f.User = ParcelServiceTests.User();
            var locker = Locker();
            var policy = Policy();
            f.Compartment = ParcelServiceTests.Compartment(locker);
            var profile = new ResidentProfile { Id = Guid.NewGuid(), UserId = f.User.Id, User = f.User, FullName = "Resident", RegisteredLockerId = locker.Id };
            f.Request = new DeliveryRequest { Id = Guid.NewGuid(), LockerId = locker.Id, Locker = locker, SystemPolicyId = policy.Id, SystemPolicy = policy, AllocatedCompartmentId = f.Compartment.Id, AllocatedCompartment = f.Compartment, ResidentProfileId = profile.Id, ResidentProfile = profile, GuestSessionTokenHash = "hash" };
            f.Parcel = new Parcel { Id = Guid.NewGuid(), DeliveryRequestId = f.Request.Id, DeliveryRequest = f.Request, ParcelCode = "P1", Status = ParcelStatus.Stored, StoredAt = Now.AddDays(-1), PickupDueAt = Now.AddDays(7), MaxStorageUntil = Now.AddDays(14) };
            f.Db.AddRange(f.User, profile, locker, policy, f.Compartment, f.Request, f.Parcel);
            await f.Db.SaveChangesAsync();
            return f;
        }
        public void AssertNoCalls()
        {
            Assert.Empty(Access.Calls);
            Assert.Empty(Push.Transfers);
            Assert.Empty(Push.Sent);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class RecordingPush : IPushNotificationService
    {
        public Guid NotificationId { get; } = Guid.NewGuid();
        public List<(Guid UserId, Guid RequestId, Guid ParcelId, string Address)> Transfers { get; } = [];
        public List<Guid> Sent { get; } = [];
        public Guid EnqueueParcelTransferred(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string collectionAddress)
        {
            Transfers.Add((residentUserId, deliveryRequestId, parcelId, collectionAddress));
            return NotificationId;
        }
        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default)
        {
            Sent.Add(notificationId);
            return Task.CompletedTask;
        }
        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode) => throw new NotSupportedException();
        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => throw new NotSupportedException();
        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode) => throw new NotSupportedException();
        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue) => throw new NotSupportedException();
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingAccess : ILockerAccessService
    {
        public List<OpenLockerRequest> Calls { get; } = [];
        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request);
            return Task.FromResult(new OpenLockerResponse(Guid.NewGuid(), request.LockerId, request.LockerCompartmentId, "DEVICE", 1, LockerAccessResult.Succeeded, null, Now));
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static User User(UserRole role = UserRole.Resident) => new() { Id = Guid.NewGuid(), PasswordHash = "hash", Email = $"{Guid.NewGuid():N}@example.com", PhoneNumber = "0912345678", Role = role, Status = UserStatus.Active, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
    private static Locker Locker(string code = "L01") => new() { Id = Guid.NewGuid(), Code = code, Address = "Address", RecoveryAddress = "Recovery", DeviceIdentifier = "DEVICE-" + code, OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
    private static LockerCompartment Compartment(Locker locker, string code = "C01") => new() { Id = Guid.NewGuid(), LockerId = locker.Id, Locker = locker, Code = code, HardwareCode = "HW-" + code, HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed };
    private static SystemPolicy Policy(int version = 1, bool active = false) => new() { Id = Guid.NewGuid(), Version = version, IsActive = active, Currency = "VND", CreatedByUserId = Guid.NewGuid(), EffectiveFrom = Now.AddDays(-1), OverdueStartAfterHours = 168, MaxStorageHours = 336, OverdueFeePerHour = 5000, EnableRemoteUnlock = true };
    private static async Task Error<T>(Func<Task> action, string message) where T : Exception
    {
        T exception = await Assert.ThrowsAsync<T>(action);
        if (exception is ArgumentException argument && argument.ParamName is not null)
            Assert.StartsWith(message, exception.Message);
        else
            Assert.Equal(message, exception.Message);
    }
    private static void Audit(ApplicationDbContext db, string action, Guid id, Guid? actor = null)
    {
        db.ChangeTracker.Clear();
        AuditLog log = Assert.Single(db.AuditLogs);
        Assert.Equal(action, log.Action);
        Assert.Equal(id, log.EntityId);
        Assert.Equal(actor, log.ActorUserId);
        Assert.Equal(AuditLogResult.Succeeded, log.Result);
    }
}
