using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Parcels;

public sealed class ParcelServiceTests
{
    #region PAR-01: GetParcelsAsync
    [Fact]
    [Trait("Category", "PAR-01")]
    public async Task GetParcelsAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        AddParcelGraph(db, owner, ParcelStatus.Stored, "P-UTC01");
        await db.SaveChangesAsync();

        var res = await CreateService(db).GetParcelsAsync(owner.Id, nameof(UserRole.Resident), ParcelListView.Active, "P-UTC01", null, null, default, 1, 10);

        Assert.Single(res.Items);
        Assert.Equal(1, res.TotalCount);
    }

    [Fact]
    [Trait("Category", "PAR-01")]
    public async Task GetParcelsAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db)
            .GetParcelsAsync(owner.Id, nameof(UserRole.Resident), ParcelListView.Active, null, now, now.AddDays(-1), default));
        Assert.Equal("From date must not be after to date.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-01")]
    public async Task GetParcelsAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db)
            .GetParcelsAsync(owner.Id, nameof(UserRole.Resident), (ParcelListView)999, null, null, null, default));
        Assert.Contains("Parcel view is invalid.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-01")]
    public async Task GetParcelsAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();

        await Assert.ThrowsAnyAsync<Exception>(() => CreateService(db)
            .GetParcelsAsync(Guid.Empty, "", (ParcelListView)999, null, null, null, default));
    }

    [Fact]
    [Trait("Category", "PAR-01")]
    public async Task GetParcelsAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        AddParcelGraph(db, owner, ParcelStatus.Stored, "P-STORED");
        AddParcelGraph(db, owner, ParcelStatus.Overdue, "P-OVERDUE");
        AddParcelGraph(db, owner, ParcelStatus.Retrieved, "P-RETRIEVED");
        AddParcelGraph(db, owner, ParcelStatus.Removed, "P-REMOVED");
        await db.SaveChangesAsync();

        var res = await CreateService(db).GetParcelsAsync(owner.Id, nameof(UserRole.Resident), ParcelListView.History, null, null, null, default);

        Assert.Equal(2, res.TotalCount);
        Assert.All(res.Items, item => Assert.True(item.Status == ParcelStatus.Retrieved || item.Status == ParcelStatus.Removed));
    }

    [Fact]
    [Trait("Category", "PAR-01")]
    public async Task GetParcelsAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel p = AddParcelGraph(db, owner, ParcelStatus.Stored, "P-TIME");
        DateTimeOffset timestamp = p.StoredAt;
        await db.SaveChangesAsync();

        var res = await CreateService(db).GetParcelsAsync(owner.Id, nameof(UserRole.Resident), ParcelListView.All, null, timestamp, timestamp, default);

        Assert.Single(res.Items);
    }

    [Fact]
    [Trait("Category", "PAR-01")]
    public async Task GetParcelsAsync_UTC07()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel p = AddParcelGraph(db, owner, ParcelStatus.Stored, "P-XYZ");
        p.DeliveryRequest.Locker.Address = "Special Street Address 999";
        await db.SaveChangesAsync();

        var res = await CreateService(db).GetParcelsAsync(owner.Id, nameof(UserRole.Resident), ParcelListView.All, "Special Street Address", null, null, default);

        Assert.Single(res.Items);
    }
    #endregion

    #region PAR-02: GetParcelAsync
    [Fact]
    [Trait("Category", "PAR-02")]
    public async Task GetParcelAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Stored, "P-UTC01");
        await db.SaveChangesAsync();

        var res = await CreateService(db).GetParcelAsync(owner.Id, nameof(UserRole.Resident), parcel.Id, default);

        Assert.Equal(parcel.Id, res.Id);
    }

    [Fact]
    [Trait("Category", "PAR-02")]
    public async Task GetParcelAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        User other = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Stored, "P-UTC02");
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(db).GetParcelAsync(other.Id, nameof(UserRole.Resident), parcel.Id, default));
        Assert.Equal("Parcel not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-02")]
    public async Task GetParcelAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(db).GetParcelAsync(owner.Id, nameof(UserRole.Resident), Guid.Empty, default));
        Assert.Equal("Parcel not found.", ex.Message);
    }
    #endregion

    #region PAR-03: GetHistoryAsync
    [Fact]
    [Trait("Category", "PAR-03")]
    public async Task GetHistoryAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Stored, "P-HIST");
        db.ParcelStatusHistories.Add(new ParcelStatusHistory
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            FromStatus = null,
            ToStatus = ParcelStatus.Stored,
            ChangedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var res = await CreateService(db).GetHistoryAsync(owner.Id, nameof(UserRole.Resident), parcel.Id, default, 1, 10);

        Assert.Single(res.Items);
    }

    [Fact]
    [Trait("Category", "PAR-03")]
    public async Task GetHistoryAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        User other = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Stored, "P-HIST");
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(db).GetHistoryAsync(other.Id, nameof(UserRole.Resident), parcel.Id, default));
        Assert.Equal("Parcel not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-03")]
    public async Task GetHistoryAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(db).GetHistoryAsync(owner.Id, nameof(UserRole.Resident), Guid.Empty, default));
        Assert.Equal("Parcel not found.", ex.Message);
    }
    #endregion

    #region PAR-04: OpenCompartmentAsync
    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Stored, "P-OPEN");
        await db.SaveChangesAsync();

        var service = new ParcelService(db, new SuccessfulAccessService(), new UnusedPushNotificationService(), TimeProvider.System);
        var res = await service.OpenCompartmentAsync(owner.Id, parcel.Id, "127.0.0.1", "device");

        Assert.Equal(parcel.Id, res.ParcelId);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);

        var service = new ParcelService(db, new SuccessfulAccessService(), new UnusedPushNotificationService(), TimeProvider.System);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.OpenCompartmentAsync(owner.Id, Guid.NewGuid(), "127.0.0.1", "device"));
        Assert.Equal("Không tìm thấy bưu kiện.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        User other = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Stored, "P-OPEN");
        await db.SaveChangesAsync();

        var service = new ParcelService(db, new SuccessfulAccessService(), new UnusedPushNotificationService(), TimeProvider.System);
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.OpenCompartmentAsync(other.Id, parcel.Id, "127.0.0.1", "device"));
        Assert.Equal("Bạn không có quyền mở ngăn chứa bưu kiện này.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Retrieved, "P-OPEN");
        await db.SaveChangesAsync();

        var service = new ParcelService(db, new SuccessfulAccessService(), new UnusedPushNotificationService(), TimeProvider.System);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(owner.Id, parcel.Id, "127.0.0.1", "device"));
        Assert.Equal("Bưu kiện không còn ở trạng thái có thể nhận.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Overdue, "P-OPEN");
        parcel.OverdueCharge = new OverdueCharge
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            Status = OverdueChargeStatus.Outstanding,
            Amount = 15000,
            Currency = "VND",
            ChargeStartAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.OverdueCharges.Add(parcel.OverdueCharge);
        await db.SaveChangesAsync();

        var service = new ParcelService(db, new SuccessfulAccessService(), new UnusedPushNotificationService(), TimeProvider.System);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(owner.Id, parcel.Id, "127.0.0.1", "device"));
        Assert.Equal("Cần thanh toán phí quá hạn trước khi nhận bưu kiện.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        var service = new ParcelService(db, new SuccessfulAccessService(), new UnusedPushNotificationService(), TimeProvider.System);

        await Assert.ThrowsAnyAsync<Exception>(() => service.OpenCompartmentAsync(Guid.Empty, Guid.Empty, null, null));
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC07()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Stored, "P-OPEN");
        parcel.DeliveryRequest.SystemPolicy.EnableRemoteUnlock = false;
        await db.SaveChangesAsync();

        var service = new ParcelService(db, new SuccessfulAccessService(), new UnusedPushNotificationService(), TimeProvider.System);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(owner.Id, parcel.Id, "127.0.0.1", "device"));
        Assert.Equal("Chính sách hiện tại không cho phép mở tủ từ ứng dụng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-04")]
    public async Task OpenCompartmentAsync_UTC08()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Stored, "P-OPEN");
        parcel.DeliveryRequest.AllocatedCompartmentId = null;
        await db.SaveChangesAsync();

        var service = new ParcelService(db, new SuccessfulAccessService(), new UnusedPushNotificationService(), TimeProvider.System);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(owner.Id, parcel.Id, "127.0.0.1", "device"));
        Assert.Equal("Bưu kiện chưa được gắn với ngăn locker.", ex.Message);
    }
    #endregion

    #region PAR-05: FinalizeRetrievalAsync
    [Fact]
    [Trait("Category", "PAR-05")]
    public async Task FinalizeRetrievalAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Stored, "P-FIN");
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.FinalizeRetrievalAsync(parcel.Id, owner.Id, DateTimeOffset.UtcNow);

        Assert.Equal(ParcelStatus.Retrieved, parcel.Status);
    }

    [Fact]
    [Trait("Category", "PAR-05")]
    public async Task FinalizeRetrievalAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.FinalizeRetrievalAsync(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Equal("Không tìm thấy bưu kiện.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-05")]
    public async Task FinalizeRetrievalAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Removed, "P-REMOVED");
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinalizeRetrievalAsync(parcel.Id, owner.Id, DateTimeOffset.UtcNow));
        Assert.Equal("Bưu kiện không còn ở trạng thái có thể hoàn tất lấy hàng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-05")]
    public async Task FinalizeRetrievalAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, owner, ParcelStatus.Retrieved, "P-ALREADY");
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.FinalizeRetrievalAsync(parcel.Id, owner.Id, DateTimeOffset.UtcNow);

        Assert.Equal(ParcelStatus.Retrieved, parcel.Status);
    }
    #endregion

    #region PAR-06: TransferOverdueAsync
    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User resident = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, resident, ParcelStatus.Overdue, "P-TRANS");
        parcel.MaxStorageUntil = DateTimeOffset.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();

        var pushService = new RecordingPushNotificationService();
        var service = new ParcelService(db, new UnusedLockerAccessService(), pushService, TimeProvider.System);

        OverdueTransferResponse res = await service.TransferOverdueAsync(Guid.NewGuid(), nameof(UserRole.Administrator), parcel.Id);

        Assert.Equal(ParcelStatus.Removed.ToString(), res.Status);
    }

    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.TransferOverdueAsync(Guid.NewGuid(), nameof(UserRole.Administrator), Guid.NewGuid()));
        Assert.Equal("Không tìm thấy kiện hàng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User resident = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, resident, ParcelStatus.Stored, "P-STORED");
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TransferOverdueAsync(Guid.NewGuid(), nameof(UserRole.Administrator), parcel.Id));
        Assert.Equal("Kiện hàng chưa đủ điều kiện chuyển điểm tập kết.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User resident = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, resident, ParcelStatus.Overdue, "P-TRANS");
        parcel.MaxStorageUntil = DateTimeOffset.UtcNow.AddMinutes(-10);
        parcel.DeliveryRequest.ResidentProfile = null;
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TransferOverdueAsync(Guid.NewGuid(), nameof(UserRole.Administrator), parcel.Id));
        Assert.Equal("Kiện hàng không có cư dân nhận.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        var service = CreateService(db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.TransferOverdueAsync(Guid.Empty, "", Guid.Empty));
    }

    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User operatorUser = CreateUser(UserRole.LockerOperator);
        User resident = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(db, resident, ParcelStatus.Overdue, "P-TRANS");
        parcel.MaxStorageUntil = DateTimeOffset.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.TransferOverdueAsync(operatorUser.Id, nameof(UserRole.LockerOperator), parcel.Id));
        Assert.Equal("Tủ không thuộc phạm vi vận hành.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PAR-06")]
    public async Task TransferOverdueAsync_UTC07()
    {
        await using ApplicationDbContext db = CreateDbContext();
        User resident = CreateUser(UserRole.Resident);
        User administrator = CreateUser(UserRole.Administrator);
        Parcel parcel = AddParcelGraph(db, resident, ParcelStatus.Overdue, "P-OVERDUE");
        parcel.MaxStorageUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
        db.Users.Add(administrator);
        await db.SaveChangesAsync();

        var pushNotifications = new RecordingPushNotificationService();
        var realtimeNotifier = new RecordingRealtimeNotifier();
        var service = new ParcelService(
            db,
            new UnusedLockerAccessService(),
            pushNotifications,
            TimeProvider.System,
            realtimeNotifier);

        OverdueTransferResponse response = await service.TransferOverdueAsync(
            administrator.Id,
            nameof(UserRole.Administrator),
            parcel.Id);

        Assert.Equal(ParcelStatus.Removed, parcel.Status);
        Assert.Equal("456 Nguyen Van Linh", response.CollectionAddress);
        Assert.Equal(resident.Id, pushNotifications.ResidentUserId);
        Assert.True(pushNotifications.WasSent);
        Assert.Equal(parcel.DeliveryRequest.LockerId, realtimeNotifier.LockerId);
        Assert.Equal("ParcelUpdated", realtimeNotifier.Message?.Type);
        Assert.Equal(ParcelStatus.Removed.ToString(), realtimeNotifier.Message?.Status);
        Assert.Contains(db.AuditLogs, log =>
            log.EntityId == parcel.Id && log.Action == "Parcel.Transferred");
    }
    #endregion

    private sealed class SuccessfulAccessService : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OpenLockerResponse(Guid.NewGuid(), request.LockerId, request.LockerCompartmentId, "SIM", 1, LockerAccessResult.Succeeded, null, DateTimeOffset.UtcNow));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ParcelService CreateService(ApplicationDbContext dbContext) =>
        new(dbContext, new UnusedLockerAccessService(), new UnusedPushNotificationService(), TimeProvider.System);

    private sealed class UnusedLockerAccessService : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedPushNotificationService : IPushNotificationService
    {
        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode) => throw new NotSupportedException();
        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode) => throw new NotSupportedException();
        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => throw new NotSupportedException();
        public Guid EnqueueParcelTransferred(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string collectionAddress) => throw new NotSupportedException();
        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue) => throw new NotSupportedException();
        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingPushNotificationService : IPushNotificationService
    {
        private readonly Guid notificationId = Guid.NewGuid();

        public Guid? ResidentUserId { get; private set; }
        public bool WasSent { get; private set; }

        public Guid EnqueueParcelTransferred(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string collectionAddress)
        {
            ResidentUserId = residentUserId;
            return notificationId;
        }

        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue) => notificationId;

        public Task TrySendAsync(Guid id, CancellationToken cancellationToken = default)
        {
            WasSent = id == notificationId;
            return Task.CompletedTask;
        }

        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode) => throw new NotSupportedException();
        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode) => throw new NotSupportedException();
        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => throw new NotSupportedException();
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingRealtimeNotifier : IOperationsRealtimeNotifier
    {
        public Guid? LockerId { get; private set; }
        public RealtimeEvent? Message { get; private set; }

        public Task PublishToLockerAsync(Guid lockerId, RealtimeEvent message, CancellationToken cancellationToken = default)
        {
            LockerId = lockerId;
            Message = message;
            return Task.CompletedTask;
        }

        public Task PublishToUserAsync(Guid userId, RealtimeEvent message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static User CreateUser(UserRole role) => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
        PasswordHash = "hash",
        Role = role,
        Status = UserStatus.Active
    };

    private static Parcel AddParcelGraph(
        ApplicationDbContext dbContext,
        User residentUser,
        ParcelStatus status,
        string parcelCode)
    {
        ResidentProfile? existingProfile = dbContext.ResidentProfiles.Local
            .SingleOrDefault(candidate => candidate.UserId == residentUser.Id);
        var profile = existingProfile ?? new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = residentUser.Id,
            FullName = "Resident",
        };
        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = $"L-{Guid.NewGuid():N}"[..10],
            Address = "123 Nguyen Van Linh",
            RecoveryAddress = "456 Nguyen Van Linh",
            DeviceIdentifier = Guid.NewGuid().ToString(),
            OperationalStatus = LockerOperationalStatus.Operational
        };
        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Code = "A01",
            HardwareCode = Guid.NewGuid().ToString(),
            HardwareChannel = 1,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational
        };
        SystemPolicy? existingPolicy = dbContext.SystemPolicies.Local.FirstOrDefault();
        var policy = existingPolicy ?? new SystemPolicy
        {
            Id = Guid.NewGuid(),
            Version = 1,
            IsActive = true,
            EnableRemoteUnlock = true,
            EffectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-5),
            Currency = "VND",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var deliveryRequest = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = profile.Id,
            ResidentProfile = profile,
            LockerId = locker.Id,
            Locker = locker,
            AllocatedCompartmentId = compartment.Id,
            AllocatedCompartment = compartment,
            SystemPolicyId = policy.Id,
            SystemPolicy = policy,
            GuestSessionTokenHash = "hash",
            Status = DeliveryRequestStatus.Deposited
        };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = deliveryRequest.Id,
            ParcelCode = parcelCode,
            Status = status,
            StoredAt = now.AddHours(-1),
            PickupDueAt = now.AddHours(23),
            MaxStorageUntil = now.AddDays(3),
            RetrievedAt = status == ParcelStatus.Retrieved ? now : null,
            DeliveryRequest = deliveryRequest
        };

        if (existingProfile is null)
        {
            dbContext.AddRange(residentUser, profile);
        }
        if (existingPolicy is null)
        {
            dbContext.Add(policy);
        }
        dbContext.AddRange(locker, compartment, deliveryRequest, parcel);
        return parcel;
    }
}
