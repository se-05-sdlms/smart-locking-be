using Microsoft.Extensions.Configuration;
using smart_locking_be.Application.Exceptions;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Application.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
namespace smart_locking_be.Tests.Lockers;

public sealed class CompartmentResetTests
{
    [Theory]
    [InlineData("Pending")]
    [InlineData("Applied")]
    [InlineData("NeedsReview")]
    public async Task Reconcile_OnlyReturnsPersistedEventsOwnedByResident_WithoutApplyingBusiness(string status)
    {
        var x = Setup(); await using var db = x.Db;
        Guid userId = Guid.NewGuid(), eventId = Guid.NewGuid(), unknownId = Guid.NewGuid();
        var profile = new ResidentProfile { Id = Guid.NewGuid(), UserId = userId, FullName = "Owner" };
        x.Request.ResidentProfile = profile; x.Request.ResidentProfileId = profile.Id;
        db.Add(profile);
        db.Add(new OfflineLockerEvent
        {
            Id = eventId,
            DeliveryId = x.Request.Id,
            DeviceIdentifier = "LKR-B",
            HardwareChannel = 1,
            Kind = "R",
            Sequence = 1,
            Status = status,
            PinCode = "123456",
            PayloadHash = "verified-mqtt"
        });
        await db.SaveChangesAsync();
        var service = new OfflinePinService(db, new BluetoothProtocol(), TimeProvider.System);

        Assert.Empty((await service.ReconcileAsync(Guid.NewGuid(), new([eventId, unknownId]))).Acknowledged);
        Assert.Empty((await service.ReconcileAsync(userId, new([unknownId]))).Acknowledged);
        var result = await service.ReconcileAsync(userId, new([eventId, eventId, unknownId]));

        var receipt = Assert.Single(result.Acknowledged);
        Assert.Equal(eventId, receipt.EventId); Assert.Equal(status, receipt.Status);
        Assert.StartsWith("k1|", receipt.Acknowledgment);
        Assert.Equal(ReturnRequestStatus.Deposited, x.Request.Status);
        Assert.Single(db.OfflineLockerEvents); Assert.Empty(db.LockerAccessEvents);
        Assert.Empty(db.AuditLogs); Assert.Empty(db.OfflineLockerCursors);
        Assert.Equal("123456", (await db.OfflineLockerEvents.SingleAsync()).PinCode);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ReconcileAsync(Guid.Empty, new([eventId])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpSync_RejectsFabricatedReturnCycle_WithoutAnyBusinessChanges(bool authenticated)
    {
        var x = Setup(); await using var db = x.Db;
        int auditCount = await db.AuditLogs.CountAsync();
        var subject = Guid.NewGuid();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var request = new OfflinePinSyncRequest([
            $"o1|{Guid.NewGuid():N}|{subject:N}|LKR-B|1|R|0900000001|123456|{now - 4}|{now - 3}|1000|1",
            $"o1|{Guid.NewGuid():N}|{subject:N}|LKR-B|1|G|0900000001|-|{now - 2}|{now - 1}|1000|2"
        ]);
        var controller = new smart_locking_be.API.Controllers.OfflinePinController(
            new OfflinePinService(db, new BluetoothProtocol(), TimeProvider.System));
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        if (authenticated)
            context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"));
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = context };

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => controller.Sync(request, default));

        Assert.Equal(smart_locking_be.Application.Errors.BluetoothErrorCodes.UnverifiedEvidence, error.ErrorCode);
        Assert.Equal(ReturnRequestStatus.Deposited, x.Request.Status);
        Assert.Single(db.ReturnRequests);
        Assert.Empty(db.OfflineLockerEvents); Assert.Empty(db.OfflineLockerCursors);
        Assert.Empty(db.LockerAccessEvents); Assert.Empty(db.LockerEvents);
        Assert.Equal(auditCount, await db.AuditLogs.CountAsync());
    }

    private sealed class Dispatcher : ICompartmentResetDispatcher
    {
        public string? Command; public bool Fail;
        public Task DispatchResetAsync(string device, int channel, string command, CancellationToken ct = default)
        { if (Fail) throw new IOException("offline"); Command = command; return Task.CompletedTask; }
    }

    private static Parcel SeedTransfer(ApplicationDbContext db, LockerCompartment slot, ReturnRequest previous)
    {
        previous.Status = ReturnRequestStatus.Cancelled;
        var owner = new ResidentProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), FullName = "Resident" };
        var delivery = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            LockerId = slot.LockerId,
            Locker = slot.Locker,
            AllocatedCompartmentId = slot.Id,
            ResidentProfileId = owner.Id,
            ResidentProfile = owner,
            RecipientPhoneSnapshot = "0900000001",
            Status = DeliveryRequestStatus.Deposited
        };
        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            ParcelCode = "P-01",
            DeliveryRequestId = delivery.Id,
            DeliveryRequest = delivery,
            Status = ParcelStatus.Overdue,
            StoredAt = DateTimeOffset.UtcNow.AddDays(-5),
            PickupDueAt = DateTimeOffset.UtcNow.AddDays(-4),
            MaxStorageUntil = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        db.AddRange(owner, delivery, parcel, new OfflineLockerPin
        {
            ParcelId = parcel.Id,
            DeliveryId = delivery.Id,
            LockerId = slot.LockerId,
            RecipientPhone = "0900000001",
            PinCode = "012345",
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
        return parcel;
    }

    [Fact]
    public async Task OverdueTransfer_ResetRevokesOnly_ExplicitArrivalReleasesAndNotifiesOnce()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid();
        var parcel = SeedTransfer(db, x.Slot, x.Request);
        var first = await x.Service.RequestTransferAsync(actor, "Administrator", parcel.Id);
        Assert.Equal("Pending", first.ResetStatus);
        Assert.Equal(ParcelStatus.Overdue, parcel.Status);
        Assert.Null((await db.OfflineLockerPins.SingleAsync()).ConsumedAt);
        Assert.Empty(db.Notifications); Assert.Empty(db.ParcelStatusHistories);
        Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, x.Slot.OperationalStatus);
        Assert.StartsWith("z1|", x.Transport.Command); Assert.DoesNotContain("OPEN", x.Transport.Command);
        var retry = await x.Service.RequestTransferAsync(actor, "Administrator", parcel.Id);
        Assert.Equal(first.ResetCommandId, retry.ResetCommandId);
        // Maintenance có thể relay lại cùng yêu cầu qua BLE, không chuyển mục đích thu hồi sang reset thường.
        var ble = await x.Service.RequestAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id,
            new(parcel.DeliveryRequestId, true, "Relay via BLE", true));
        Assert.Equal(first.ResetCommandId, ble.CommandId);
        var proof = $"z2|{ble.CommandId:N}|LKR-B|1|{parcel.DeliveryRequestId:N}|OPEN|OK";
        await x.Service.ReceiveAsync("LKR-B", proof);
        Assert.Equal(ParcelStatus.Overdue, parcel.Status);
        Assert.Equal(DoorStatus.Open, x.Slot.DoorStatus); // Không giả cửa đóng để hiển thị Ready.
        var pin = await db.OfflineLockerPins.SingleAsync(); Assert.NotNull(pin.ConsumedAt); Assert.Empty(pin.PinCode);
        Assert.Null(parcel.DeliveryRequest.CompartmentReleasedAt);
        Assert.Empty(db.ParcelStatusHistories); Assert.Empty(db.Notifications);
        // Niềm tin ACK reset không được chuyển thành bằng chứng hàng đã đến điểm tập kết.
        var statusOnly = await x.Service.RequestTransferAsync(actor, "Administrator", parcel.Id);
        Assert.Equal("Applied", statusOnly.ResetStatus); Assert.Equal("Overdue", statusOnly.Status);
        Assert.Equal(ParcelStatus.Overdue, parcel.Status); Assert.Empty(db.Notifications);
        var arrived = await x.Service.ConfirmTransferArrivalAsync(actor, "Administrator", parcel.Id, new(ble.CommandId, true));
        Assert.Equal("Removed", arrived.Status); Assert.NotNull(parcel.DeliveryRequest.CompartmentReleasedAt);
        Assert.Single(db.ParcelStatusHistories);
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.Type == "ParcelTransferred"));
        Assert.Single(db.AuditLogs.Where(a => a.Action == "Parcel.Transferred"));
        await x.Service.ReceiveAsync("LKR-B", proof);
        var final = await x.Service.RequestTransferAsync(actor, "Administrator", parcel.Id);
        await x.Service.ConfirmTransferArrivalAsync(actor, "Administrator", parcel.Id, new(ble.CommandId, true));
        Assert.Equal("Applied", final.ResetStatus); Assert.Single(db.ParcelStatusHistories);
        Assert.Equal(2, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task OverdueTransfer_OfflineWorkerReusesDurableCommand_AndFailureDoesNotRelease()
    {
        var x = Setup(); await using var db = x.Db; var parcel = SeedTransfer(db, x.Slot, x.Request);
        x.Transport.Fail = true;
        var first = await x.Service.RequestTransferAsync(Guid.NewGuid(), "Administrator", parcel.Id);
        Assert.Equal("Pending", first.ResetStatus); Assert.Equal(ParcelStatus.Overdue, parcel.Status);
        db.ChangeTracker.Clear(); // Chỉ còn dữ liệu đã lưu, không dựa vào state trong service/request cũ.
        x.Transport.Fail = false;
        var restarted = new CompartmentResetService(new CompartmentResetStore(db), x.Transport, new BluetoothProtocol(), TimeProvider.System);
        await restarted.RetryPendingTransfersAsync();
        Assert.Contains(first.ResetCommandId!.Value.ToString("N"), x.Transport.Command);
        await restarted.ReceiveAsync("LKR-B", $"z2|{first.ResetCommandId:N}|LKR-B|1|{parcel.DeliveryRequestId:N}|CLOSED|FAIL_SYNC_REQUIRED");
        Assert.Equal(ParcelStatus.Overdue, (await db.Parcels.SingleAsync()).Status);
        Assert.Null((await db.OfflineLockerPins.SingleAsync()).ConsumedAt); Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task OverdueTransfer_CanonicalParcelServiceReusesPendingRequest_AndCannotReenableSlot()
    {
        var x = Setup(); await using var db = x.Db; var parcel = SeedTransfer(db, x.Slot, x.Request); var actor = Guid.NewGuid();
        var parcels = new ParcelService(db, TimeProvider.System, x.Service);
        var first = await parcels.TransferOverdueAsync(actor, "Administrator", parcel.Id);
        var second = await parcels.TransferOverdueAsync(actor, "Administrator", parcel.Id);
        Assert.Equal(first.ResetCommandId, second.ResetCommandId);
        Assert.Single(db.AuditLogs.Where(a => a.Action == "LockerCompartment.ResetRequested")); Assert.Equal(ParcelStatus.Overdue, parcel.Status);
        await Assert.ThrowsAsync<ConflictException>(() => new LockerService(db).UpdateCompartmentStatusAsync(actor, "Administrator",
            x.Slot.LockerId, x.Slot.Id, new(LockerCompartmentOperationalStatus.Operational)));
    }

    [Fact]
    public async Task OverdueTransfer_RejectsTooEarly_AndUnassignedOperator()
    {
        var x = Setup(); await using var db = x.Db; var parcel = SeedTransfer(db, x.Slot, x.Request);
        int initialAudits = await db.AuditLogs.CountAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Service.RequestTransferAsync(Guid.NewGuid(), "LockerOperator", parcel.Id));
        parcel.MaxStorageUntil = DateTimeOffset.UtcNow.AddDays(1); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => x.Service.RequestTransferAsync(Guid.NewGuid(), "Administrator", parcel.Id));
        Assert.Equal(initialAudits, await db.AuditLogs.CountAsync()); Assert.Null(x.Transport.Command);
    }

    [Fact]
    public async Task RemovedWithoutResetReceipt_IsNotReportedAsPinRevoked()
    {
        var x = Setup(); await using var db = x.Db; var parcel = SeedTransfer(db, x.Slot, x.Request);
        parcel.Status = ParcelStatus.Removed; parcel.RemovalReason = "Transferred to overdue collection point";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => x.Service.RequestTransferAsync(Guid.NewGuid(), "Administrator", parcel.Id));
        Assert.Null((await db.OfflineLockerPins.SingleAsync()).ConsumedAt); Assert.Null(x.Transport.Command);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatePickupDuringRecovery_IsReviewedWithoutInventingPhysicalOutcome(bool arrivalConfirmed)
    {
        var x = Setup(); await using var db = x.Db; var parcel = SeedTransfer(db, x.Slot, x.Request);
        var result = await x.Service.RequestTransferAsync(Guid.NewGuid(), "Administrator", parcel.Id);
        await x.Service.ReceiveAsync("LKR-B", $"z2|{result.ResetCommandId:N}|LKR-B|1|{parcel.DeliveryRequestId:N}|CLOSED|OK");
        if (arrivalConfirmed)
            await x.Service.ConfirmTransferArrivalAsync(Guid.NewGuid(), "Administrator", parcel.Id, new(result.ResetCommandId!.Value, true));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var offline = new OfflinePinService(db, new BluetoothProtocol(), TimeProvider.System);
        var late = await offline.ReceiveAsync("LKR-B", $"o1|{Guid.NewGuid():N}|{parcel.DeliveryRequestId:N}|LKR-B|1|P|0900000001|-|{now - 2}|{now}|2000|1");
        Assert.Equal("NeedsReview", late.Status);
        Assert.Equal(arrivalConfirmed ? ParcelStatus.Removed : ParcelStatus.Overdue, parcel.Status);
        Assert.Equal(arrivalConfirmed ? 1 : 0, await db.ParcelStatusHistories.CountAsync());
        Assert.Empty(db.LockerAccessEvents.Where(x => x.Result == LockerAccessResult.Succeeded));
        if (!arrivalConfirmed)
            await Assert.ThrowsAsync<ConflictException>(() => x.Service.ConfirmTransferArrivalAsync(Guid.NewGuid(), "Administrator",
                parcel.Id, new(result.ResetCommandId!.Value, true)));
    }

    [Fact]
    public async Task RecoveryArrival_RequiresExplicitConfirmation_AppliedReset_AndAssignedOperator()
    {
        var x = Setup(); await using var db = x.Db;
        var parcel = SeedTransfer(db, x.Slot, x.Request);
        var response = await x.Service.RequestTransferAsync(Guid.NewGuid(), "Administrator", parcel.Id);
        var resetId = response.ResetCommandId!.Value;
        await Assert.ThrowsAsync<BadRequestException>(() => x.Service.ConfirmTransferArrivalAsync(Guid.NewGuid(), "Administrator", parcel.Id, new(resetId, false)));
        await Assert.ThrowsAsync<ConflictException>(() => x.Service.ConfirmTransferArrivalAsync(Guid.NewGuid(), "Administrator", parcel.Id, new(resetId, true)));
        await x.Service.ReceiveAsync("LKR-B", $"z2|{resetId:N}|LKR-B|1|{parcel.DeliveryRequestId:N}|CLOSED|OK");
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Service.ConfirmTransferArrivalAsync(Guid.NewGuid(), "LockerOperator", parcel.Id, new(resetId, true)));
        Assert.Equal(ParcelStatus.Overdue, parcel.Status); Assert.Empty(db.Notifications);
        Assert.Null(parcel.DeliveryRequest.CompartmentReleasedAt);
        var confirmed = await new ParcelService(db, TimeProvider.System, x.Service)
            .ConfirmOverdueArrivalAsync(Guid.NewGuid(), "Administrator", parcel.Id, new(resetId, true));
        Assert.True(confirmed.ArrivalConfirmed); Assert.Equal(ParcelStatus.Removed, parcel.Status);
    }

    [Fact]
    public async Task PickupConfirmation_ClosedDoorAndDispatchAlone_CannotMarkRetrieved()
    {
        var x = Setup(); await using var db = x.Db; var parcel = SeedTransfer(db, x.Slot, x.Request);
        var access = new LockerAccessEvent
        {
            Id = Guid.NewGuid(),
            LockerId = x.Slot.LockerId,
            LockerCompartmentId = x.Slot.Id,
            ParcelId = parcel.Id,
            UserId = parcel.DeliveryRequest.ResidentProfile!.UserId,
            AccessType = LockerAccessType.ResidentPickup,
            AccessMethod = LockerAccessMethod.Bluetooth,
            Result = LockerAccessResult.Succeeded,
            OccurredAt = DateTimeOffset.UtcNow
        };
        db.LockerAccessEvents.Add(access); await db.SaveChangesAsync();
        var pickups = new ParcelPickupService(db, null!, TimeProvider.System);
        await Assert.ThrowsAsync<ConflictException>(() => pickups.ConfirmPickupAsync(access.UserId!.Value, access.Id));
        Assert.Equal(ParcelStatus.Overdue, parcel.Status); Assert.Empty(db.ParcelStatusHistories);
        Assert.Null((await db.OfflineLockerPins.SingleAsync()).ConsumedAt);
    }

    [Theory]
    [InlineData("LKR-B", 1)]
    [InlineData("UNKNOWN", 0)]
    public async Task OfflineReview_HasAuditWithoutFabricatedSuccess_AndReplayIsIdempotent(string device, int accessCount)
    {
        var x = Setup(); await using var db = x.Db; var subject = Guid.NewGuid(); var eventId = Guid.NewGuid();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var proof = $"o1|{eventId:N}|{subject:N}|{device}|1|P|0900000001|-|{now - 2}|{now}|2000|1";
        var service = new OfflinePinService(db, new BluetoothProtocol(), TimeProvider.System);
        var first = await service.ReceiveAsync(device, proof);
        Assert.Equal("NeedsReview", first.Status);
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action.StartsWith("OfflinePin."))); // Received + NeedsReview.
        int initialAudits = await db.AuditLogs.CountAsync();
        Assert.Equal(accessCount, await db.LockerAccessEvents.CountAsync());
        Assert.DoesNotContain(db.LockerAccessEvents, a => a.Result == LockerAccessResult.Succeeded);
        await service.ReceiveAsync(device, proof);
        Assert.Equal(initialAudits, await db.AuditLogs.CountAsync()); Assert.Single(db.Incidents);
    }
    private static (ApplicationDbContext Db, Dispatcher Transport, CompartmentResetService Service, LockerCompartment Slot, ReturnRequest Request) Setup()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var locker = new Locker { Id = Guid.NewGuid(), Code = "B", DeviceIdentifier = "LKR-B", Address = "B", RecoveryAddress = "B", OperationalStatus = LockerOperationalStatus.Operational };
        var slot = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            Locker = locker,
            LockerId = locker.Id,
            Code = "B1",
            HardwareCode = "CH1",
            HardwareChannel = 1,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Closed
        };
        var request = new ReturnRequest { Id = Guid.NewGuid(), LockerId = locker.Id, AllocatedCompartmentId = slot.Id, ReturnCode = "123456", Status = ReturnRequestStatus.Deposited };
        db.AddRange(locker, slot, request); db.SaveChanges();
        var transport = new Dispatcher();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bluetooth:CommandSigningKeys:LKR-B"] = new string('1', 64) }).Build();
        return (db, transport, new(new CompartmentResetStore(db), transport, new BluetoothProtocol(), TimeProvider.System,
            new HmacBluetoothCommandSigner(configuration)), slot, request);
    }
    [Theory]
    [InlineData("Resident")]
    [InlineData("Guest")]
    [InlineData("LockerOperator")]
    public async Task UnauthorizedActor_CannotSendOrChangeState(string role)
    {
        var x = Setup(); await using var db = x.Db;
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Service.RequestAsync(Guid.NewGuid(), role, x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Đã lấy hàng")));
        Assert.Null(x.Transport.Command); Assert.Empty(db.AuditLogs); Assert.Equal(LockerCompartmentOperationalStatus.Operational, x.Slot.OperationalStatus);
    }
    [Fact]
    public async Task AssignedOperator_CanRequest_ButCannotCompleteBeforeReceipt()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid();
        db.OperatorAssignments.Add(new() { Id = Guid.NewGuid(), LockerId = x.Slot.LockerId, OperatorUserId = actor }); await db.SaveChangesAsync();
        var response = await x.Service.RequestAsync(actor, "LockerOperator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Ngăn trống"));
        Assert.Equal("Pending", response.Status); Assert.StartsWith("z1|", x.Transport.Command);
        Assert.Equal(ReturnRequestStatus.Deposited, x.Request.Status);
        Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, x.Slot.OperationalStatus);
        Assert.Empty(db.LockerEvents);
        var proof = $"z2|{response.CommandId:N}|LKR-B|1|{x.Request.Id:N}|OPEN|OK";
        var unverified = await x.Service.ConfirmAsync(actor, "LockerOperator", response.CommandId, proof);
        Assert.Equal("Pending", unverified.Status);
        Assert.Equal(ReturnRequestStatus.Deposited, x.Request.Status);
        Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, x.Slot.OperationalStatus);
        Assert.Empty(db.LockerEvents);
        // Chỉ adapter MQTT tin cậy mới được áp dụng ACK. Chuỗi z2 từ HTTP không phải bằng chứng ESP.
        await x.Service.ReceiveAsync("LKR-B", proof);
        Assert.Equal(ReturnRequestStatus.Deposited, x.Request.Status);
        Assert.Null(x.Request.CompartmentReleasedAt); Assert.Empty(db.ParcelStatusHistories);
        Assert.Equal(DoorStatus.Open, x.Slot.DoorStatus);
        Assert.Equal(LockerCompartmentOperationalStatus.Operational, x.Slot.OperationalStatus);
        Assert.Equal("Applied", (await x.Service.GetAsync(actor, "LockerOperator", response.CommandId)).Status);
        await x.Service.ReceiveAsync("LKR-B", proof);
        Assert.Single(db.LockerEvents); Assert.Single(db.ReturnRequests); // Giữ lịch sử, không tạo thêm khi ACK lặp.
    }
    [Fact]
    public async Task MissingEmptyConfirmation_AndWrongSubject_AreRejected()
    {
        var x = Setup(); await using var db = x.Db;
        await Assert.ThrowsAsync<BadRequestException>(() => x.Service.RequestAsync(Guid.NewGuid(), "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, false, "Không có xác nhận")));
        await Assert.ThrowsAsync<ConflictException>(() => x.Service.RequestAsync(Guid.NewGuid(), "Administrator", x.Slot.LockerId, x.Slot.Id, new(Guid.NewGuid(), true, "Sai giao dịch")));
        Assert.Empty(db.AuditLogs);
    }
    [Fact]
    public async Task WrongReceipt_DoesNotReleaseSlot_OrInvalidatePin()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid();
        var command = await x.Service.RequestAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Đã kiểm tra", true));
        await Assert.ThrowsAsync<BadRequestException>(() => x.Service.ReceiveAsync("OTHER", $"z2|{command.CommandId:N}|OTHER|1|{x.Request.Id:N}|CLOSED|OK"));
        Assert.Equal(ReturnRequestStatus.Deposited, x.Request.Status); Assert.Empty(db.LockerEvents);
        await x.Service.ReceiveAsync("LKR-B", $"z2|{command.CommandId:N}|LKR-B|1|{x.Request.Id:N}|CLOSED|FAIL_SYNC_REQUIRED");
        Assert.Equal("Pending", (await x.Service.GetAsync(actor, "Administrator", command.CommandId)).Status);
        Assert.Equal(ReturnRequestStatus.Deposited, x.Request.Status);
    }
    [Fact]
    public async Task OfflineAndRetry_ReuseRequest_AndKeepSlotUnavailable()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid(); x.Transport.Fail = true;
        var first = await x.Service.RequestAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Đã lấy hết đồ"));
        var retry = await x.Service.RequestAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Thử lại", true));
        Assert.Equal(first.CommandId, retry.CommandId); Assert.NotNull(retry.Command);
        Assert.Single(db.AuditLogs.Where(a => a.Action == "LockerCompartment.ResetRequested"));
        await Assert.ThrowsAsync<ConflictException>(() => new LockerService(db).UpdateCompartmentStatusAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id, new(LockerCompartmentOperationalStatus.Operational)));
    }
    [Fact]
    public async Task OtherActiveTransaction_PreventsReset_AndLateReceiptCannotClearIt()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid();
        var response = await x.Service.RequestAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Trống", true));
        db.ReturnRequests.Add(new() { Id = Guid.NewGuid(), LockerId = x.Slot.LockerId, AllocatedCompartmentId = x.Slot.Id, Status = ReturnRequestStatus.Deposited, ReturnCode = "654321" }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => x.Service.ReceiveAsync("LKR-B", $"z2|{response.CommandId:N}|LKR-B|1|{x.Request.Id:N}|CLOSED|OK"));
        Assert.Empty(db.LockerEvents); Assert.Equal(ReturnRequestStatus.Deposited, x.Request.Status);
    }
    [Fact]
    public async Task RevokedAssignment_CannotReadOrConfirmReset()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid();
        db.OperatorAssignments.Add(new() { Id = Guid.NewGuid(), LockerId = x.Slot.LockerId, OperatorUserId = actor, RevokedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        var response = await x.Service.RequestAsync(Guid.NewGuid(), "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Đã kiểm tra", true));
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Service.GetAsync(actor, "LockerOperator", response.CommandId));
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Service.ConfirmAsync(actor, "LockerOperator", response.CommandId, $"z2|{response.CommandId:N}|LKR-B|1|{x.Request.Id:N}|CLOSED|OK"));
    }
    [Fact]
    public async Task ReceiptFromOldHardwareMapping_CannotApplyToChangedCompartment()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid();
        var response = await x.Service.RequestAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Đã lấy hết hàng", true));
        x.Slot.HardwareChannel = 2; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BadRequestException>(() => x.Service.ReceiveAsync("LKR-B", $"z2|{response.CommandId:N}|LKR-B|1|{x.Request.Id:N}|CLOSED|OK"));
        Assert.Empty(db.LockerEvents);
        Assert.Equal(ReturnRequestStatus.Deposited, x.Request.Status);
    }
    [Fact]
    public async Task SimultaneousMqttAndBleReceipt_AppliesOnlyOnce()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid();
        var response = await x.Service.RequestAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Đã lấy hết hàng", true));
        string proof = $"z2|{response.CommandId:N}|LKR-B|1|{x.Request.Id:N}|CLOSED|OK";
        await Task.WhenAll(x.Service.ReceiveAsync("LKR-B", proof), x.Service.ReceiveAsync("LKR-B", proof));
        Assert.Single(db.LockerEvents); Assert.Equal(ReturnRequestStatus.Deposited, x.Request.Status);
        Assert.Null(x.Request.CompartmentReleasedAt);
    }

}
