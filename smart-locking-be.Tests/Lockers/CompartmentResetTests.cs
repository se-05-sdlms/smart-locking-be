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
    private sealed class Dispatcher : ICompartmentResetDispatcher
    {
        public string? Command; public bool Fail;
        public Task DispatchResetAsync(string device, int channel, string command, CancellationToken ct = default)
        { if (Fail) throw new IOException("offline"); Command = command; return Task.CompletedTask; }
    }
    private static (ApplicationDbContext Db, Dispatcher Transport, CompartmentResetService Service, LockerCompartment Slot, ReturnRequest Request) Setup()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var locker = new Locker { Id = Guid.NewGuid(), Code = "B", DeviceIdentifier = "LKR-B", Address = "B", RecoveryAddress = "B", OperationalStatus = LockerOperationalStatus.Operational };
        var slot = new LockerCompartment { Id = Guid.NewGuid(), Locker = locker, LockerId = locker.Id, Code = "B1", HardwareCode = "CH1", HardwareChannel = 1,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed };
        var request = new ReturnRequest { Id = Guid.NewGuid(), LockerId = locker.Id, AllocatedCompartmentId = slot.Id, ReturnCode = "123456", Status = ReturnRequestStatus.Deposited };
        db.AddRange(locker, slot, request); db.SaveChanges();
        var transport = new Dispatcher();return (db, transport, new(new CompartmentResetStore(db), transport, new BluetoothProtocol(), TimeProvider.System), slot, request);
    }
    [Theory]
    [InlineData("Resident")]
    [InlineData("Guest")]
    [InlineData("LockerOperator")]
    public async Task UnauthorizedActor_CannotSendOrChangeState(string role)
    {
        var x = Setup(); await using var db = x.Db;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => x.Service.RequestAsync(Guid.NewGuid(), role, x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Đã lấy hàng")));
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
        await x.Service.ConfirmAsync(actor, "LockerOperator", response.CommandId, proof);
        Assert.Equal(ReturnRequestStatus.Cancelled, x.Request.Status);
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
        await Assert.ThrowsAsync<ArgumentException>(() => x.Service.RequestAsync(Guid.NewGuid(), "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, false, "Không có xác nhận")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => x.Service.RequestAsync(Guid.NewGuid(), "Administrator", x.Slot.LockerId, x.Slot.Id, new(Guid.NewGuid(), true, "Sai giao dịch")));
        Assert.Empty(db.AuditLogs);
    }
    [Fact]
    public async Task WrongReceipt_DoesNotReleaseSlot_OrInvalidatePin()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid();
        var command = await x.Service.RequestAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Đã kiểm tra", true));
        await Assert.ThrowsAsync<ArgumentException>(() => x.Service.ReceiveAsync("OTHER", $"z2|{command.CommandId:N}|OTHER|1|{x.Request.Id:N}|CLOSED|OK"));
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
        Assert.Equal(first.CommandId, retry.CommandId); Assert.NotNull(retry.Command); Assert.Single(db.AuditLogs);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LockerService(db).UpdateCompartmentStatusAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id, new(LockerCompartmentOperationalStatus.Operational)));
    }
    [Fact]
    public async Task OtherActiveTransaction_PreventsReset_AndLateReceiptCannotClearIt()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid();
        var response = await x.Service.RequestAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Trống", true));
        db.ReturnRequests.Add(new() { Id = Guid.NewGuid(), LockerId = x.Slot.LockerId, AllocatedCompartmentId = x.Slot.Id, Status = ReturnRequestStatus.Deposited, ReturnCode = "654321" }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => x.Service.ReceiveAsync("LKR-B", $"z2|{response.CommandId:N}|LKR-B|1|{x.Request.Id:N}|CLOSED|OK"));
        Assert.Empty(db.LockerEvents); Assert.Equal(ReturnRequestStatus.Deposited, x.Request.Status);
    }
    [Fact]
    public async Task RevokedAssignment_CannotReadOrConfirmReset()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid();
        db.OperatorAssignments.Add(new() { Id = Guid.NewGuid(), LockerId = x.Slot.LockerId, OperatorUserId = actor, RevokedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        var response = await x.Service.RequestAsync(Guid.NewGuid(), "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Đã kiểm tra", true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => x.Service.GetAsync(actor, "LockerOperator", response.CommandId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => x.Service.ConfirmAsync(actor, "LockerOperator", response.CommandId, $"z2|{response.CommandId:N}|LKR-B|1|{x.Request.Id:N}|CLOSED|OK"));
    }    [Fact]
    public async Task ReceiptFromOldHardwareMapping_CannotApplyToChangedCompartment()
    {
        var x = Setup(); await using var db = x.Db; var actor = Guid.NewGuid();
        var response = await x.Service.RequestAsync(actor, "Administrator", x.Slot.LockerId, x.Slot.Id, new(x.Request.Id, true, "Đã lấy hết hàng", true));
        x.Slot.HardwareChannel = 2; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => x.Service.ReceiveAsync("LKR-B", $"z2|{response.CommandId:N}|LKR-B|1|{x.Request.Id:N}|CLOSED|OK"));
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
        Assert.Single(db.LockerEvents); Assert.Equal(ReturnRequestStatus.Cancelled, x.Request.Status);
    }

}
