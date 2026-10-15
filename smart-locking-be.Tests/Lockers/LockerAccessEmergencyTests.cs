using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class LockerAccessEmergencyTests
{
    [Fact]
    [Trait("Category", "EMG-01")]
    public async Task CreateAsync_UTC01()
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        var access = new RecordingAccess();
        var service = new EmergencyUnlockService(db, access, new Clock());
        var request = new EmergencyUnlockRequest(locker.Id, compartment.Id, null, " Emergency reason ");
        var result = await service.CreateAsync(admin, "Administrator", request);
        Assert.Equal(EmergencyUnlockResult.Succeeded, result.Result);
        Assert.Equal(compartment.Code, result.CompartmentCode);
        Assert.Equal(Now, result.RequestedAt);
        db.ChangeTracker.Clear();
        var saved = Assert.Single(db.EmergencyUnlocks);
        Assert.Equal(result.Id, saved.Id);
        Assert.Equal("Emergency reason", saved.Reason);
        Assert.Equal(Now, saved.CompletedAt);
        var call = Assert.Single(access.Calls);
        Assert.Equal(compartment.Id, call.LockerCompartmentId);
        Assert.Equal(admin, call.UserId);
        Assert.Equal(LockerAccessType.OperatorEmergency, call.AccessType);
        Audit(db, "Locker.EmergencyUnlock", compartment.Id, admin);
    }

    [Fact]
    [Trait("Category", "EMG-01")]
    public async Task CreateAsync_UTC02()
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        var access = new RecordingAccess();
        var service = new EmergencyUnlockService(db, access, new Clock());
        var request = new EmergencyUnlockRequest(locker.Id, compartment.Id, null, " Emergency reason ");
        await Error<ArgumentException>(() => service.CreateAsync(admin, "Administrator", request with { Reason = "" }), "Lý do mở khẩn cấp là bắt buộc.");
        Assert.Empty(access.Calls);
        Assert.Empty(db.EmergencyUnlocks);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "EMG-01")]
    public async Task CreateAsync_UTC03()
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        var access = new RecordingAccess();
        var service = new EmergencyUnlockService(db, access, new Clock());
        var request = new EmergencyUnlockRequest(locker.Id, compartment.Id, null, " Emergency reason ");
        await Error<KeyNotFoundException>(() => service.CreateAsync(admin, "Administrator", request with { CompartmentId = Guid.NewGuid() }), "Không tìm thấy ngăn tủ.");
        Assert.Empty(access.Calls);
        Assert.Empty(db.EmergencyUnlocks);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "EMG-01")]
    public async Task CreateAsync_UTC04()
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        var access = new RecordingAccess();
        var service = new EmergencyUnlockService(db, access, new Clock());
        var request = new EmergencyUnlockRequest(locker.Id, compartment.Id, null, " Emergency reason ");
        await Error<ArgumentException>(() => service.CreateAsync(admin, "Administrator", request with { IncidentId = Guid.NewGuid() }), "Sự cố không thuộc đúng tủ hoặc ngăn được mở khẩn cấp.");
        Assert.Empty(access.Calls);
        Assert.Empty(db.EmergencyUnlocks);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "EMG-01")]
    public async Task CreateAsync_UTC05()
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        var access = new RecordingAccess();
        var service = new EmergencyUnlockService(db, access, new Clock());
        var request = new EmergencyUnlockRequest(locker.Id, compartment.Id, null, " Emergency reason ");
        await Assert.ThrowsAsync<NullReferenceException>(() => service.CreateAsync(Guid.Empty, "", null!));
        Assert.Empty(access.Calls);
        Assert.Empty(db.EmergencyUnlocks);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC01()
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        var dispatcher = new RecordingDispatcher();
        var service = new LockerAccessService(db, dispatcher, new Clock());
        var request = new OpenLockerRequest(locker.Id, compartment.Id, null, Guid.NewGuid(), null, null, LockerAccessType.ShipperDropOff, LockerAccessMethod.GuestSession, " 127.0.0.1 ", " Guest Web ");
        var result = await service.OpenAsync(request);
        Assert.Equal(LockerAccessResult.Succeeded, result.Result);
        Assert.Null(result.FailureReason);
        Assert.Equal(Now, result.OccurredAt);
        var call = Assert.Single(dispatcher.Commands);
        Assert.Equal(result.AccessEventId, call.CommandId);
        Assert.Equal(locker.DeviceIdentifier, call.DeviceIdentifier);
        Assert.Equal(compartment.HardwareChannel, call.HardwareChannel);
        db.ChangeTracker.Clear();
        var saved = Assert.Single(db.LockerAccessEvents);
        Assert.Equal(result.AccessEventId, saved.Id);
        Assert.Equal(request.DeliveryRequestId, saved.DeliveryRequestId);
        Assert.Equal("127.0.0.1", saved.IpAddress);
        Assert.Equal("Guest Web", saved.DeviceContext);
        Assert.Equal(LockerAccessResult.Succeeded, saved.Result);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC02()
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        var dispatcher = new RecordingDispatcher();
        var service = new LockerAccessService(db, dispatcher, new Clock());
        var request = new OpenLockerRequest(locker.Id, compartment.Id, null, Guid.NewGuid(), null, null, LockerAccessType.ShipperDropOff, LockerAccessMethod.GuestSession, " 127.0.0.1 ", " Guest Web ");
        await Error<KeyNotFoundException>(() => service.OpenAsync(request with { LockerCompartmentId = Guid.NewGuid() }), "Không tìm thấy ngăn locker.");
        Assert.Empty(dispatcher.Commands);
        Assert.Empty(db.LockerAccessEvents);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC03()
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        var dispatcher = new RecordingDispatcher();
        var service = new LockerAccessService(db, dispatcher, new Clock());
        var request = new OpenLockerRequest(locker.Id, compartment.Id, null, Guid.NewGuid(), null, null, LockerAccessType.ShipperDropOff, LockerAccessMethod.GuestSession, " 127.0.0.1 ", " Guest Web ");
        await Error<ArgumentException>(() => service.OpenAsync(request with { LockerId = Guid.NewGuid() }), "Ngăn locker không thuộc locker được yêu cầu.");
        Assert.Empty(dispatcher.Commands);
        Assert.Empty(db.LockerAccessEvents);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC04()
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        var dispatcher = new RecordingDispatcher();
        var service = new LockerAccessService(db, dispatcher, new Clock());
        var request = new OpenLockerRequest(locker.Id, compartment.Id, null, Guid.NewGuid(), null, null, LockerAccessType.ShipperDropOff, LockerAccessMethod.GuestSession, " 127.0.0.1 ", " Guest Web ");
        await Assert.ThrowsAsync<NullReferenceException>(() => service.OpenAsync(null!));
        Assert.Empty(dispatcher.Commands);
        Assert.Empty(db.LockerAccessEvents);
    }

    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC01()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mqtt:Simulate"] = "true" }).Build();
        var service = new MqttLockerCommandDispatcher(config);
        await service.DispatchUnlockAsync(new(Guid.NewGuid(), "DEVICE", 1));
    }

    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC02()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mqtt:Simulate"] = "false" }).Build();
        var service = new MqttLockerCommandDispatcher(config);
        await Error<InvalidOperationException>(() => service.DispatchUnlockAsync(new(Guid.NewGuid(), "DEVICE", 1)), "MQTT broker chưa được cấu hình.");
    }

    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC03()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mqtt:Simulate"] = "true" }).Build();
        var service = new MqttLockerCommandDispatcher(config);
        await service.DispatchUnlockAsync(null!);
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
    private sealed class RecordingDispatcher : ILockerCommandDispatcher
    {
        public List<LockerUnlockCommand> Commands { get; } = []; public Task DispatchUnlockAsync(LockerUnlockCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.CompletedTask;
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static Locker Locker(string code = "L01") => new() { Id = Guid.NewGuid(), Code = code, Address = "Address", RecoveryAddress = "Recovery", DeviceIdentifier = "DEVICE-" + code, OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
    private static LockerCompartment Compartment(Locker locker, string code = "C01") => new() { Id = Guid.NewGuid(), LockerId = locker.Id, Locker = locker, Code = code, HardwareCode = "HW-" + code, HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed };
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
