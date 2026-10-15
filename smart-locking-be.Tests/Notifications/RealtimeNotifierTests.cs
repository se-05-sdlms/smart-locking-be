using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.API.Hubs;
using smart_locking_be.API.Services;
using smart_locking_be.Application.DTOs.Notifications;

namespace smart_locking_be.Tests.Notifications;

public sealed class RealtimeNotifierTests
{
    private static readonly RealtimeEvent Message = new("ParcelStored", Guid.NewGuid(), Guid.NewGuid(), "Stored", "Stored in locker", DateTimeOffset.Parse("2026-10-15T08:00:00Z"));
    [Fact]
    [Trait("Category", "RTN-01")]
    public async Task PublishToUserAsync_UTC01()
    {
        var hub = new Hub(); var id = Guid.NewGuid(); using var source = new CancellationTokenSource();
        await Create(hub).PublishToUserAsync(id, Message, source.Token);
        Assert.Equal($"user:{id:N}", hub.ClientsRecorder.GroupName); AssertMessage(hub, Message, source.Token);
    }
    [Fact]
    [Trait("Category", "RTN-01")]
    public async Task PublishToUserAsync_UTC02()
    {
        // SignalR does not query users; an empty group is a successful no-op transport send.
        var hub = new Hub(); var id = Guid.NewGuid(); await Create(hub).PublishToUserAsync(id, Message); Assert.Equal($"user:{id:N}", hub.ClientsRecorder.GroupName); Assert.Equal(1, hub.ClientsRecorder.Proxy.Calls);
    }
    [Fact]
    [Trait("Category", "RTN-01")]
    public async Task PublishToUserAsync_UTC03()
    { var hub = new Hub(); await Create(hub).PublishToUserAsync(Guid.Empty, null!); Assert.Equal($"user:{Guid.Empty:N}", hub.ClientsRecorder.GroupName); AssertMessage(hub, null, default); }
    [Fact]
    [Trait("Category", "RTN-02")]
    public async Task PublishToLockerAsync_UTC01()
    { var hub = new Hub(); var id = Guid.NewGuid(); await Create(hub).PublishToLockerAsync(id, Message); Assert.Equal($"locker:{id:N}", hub.ClientsRecorder.GroupName); AssertMessage(hub, Message, default); }
    [Fact]
    [Trait("Category", "RTN-02")]
    public async Task PublishToLockerAsync_UTC02()
    { var hub = new Hub(); var id = Guid.NewGuid(); await Create(hub).PublishToLockerAsync(id, Message); Assert.Equal($"locker:{id:N}", hub.ClientsRecorder.GroupName); Assert.Equal(1, hub.ClientsRecorder.Proxy.Calls); }
    [Fact]
    [Trait("Category", "RTN-02")]
    public async Task PublishToLockerAsync_UTC03()
    { var hub = new Hub(); await Create(hub).PublishToLockerAsync(Guid.Empty, null!); Assert.Equal($"locker:{Guid.Empty:N}", hub.ClientsRecorder.GroupName); AssertMessage(hub, null, default); }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "RTN-01")]
    [Trait("Category", "RTN-02")]
    public async Task PublishAsync_ContainsTransportFailures(bool user)
    {
        var hub = new Hub(); hub.ClientsRecorder.Proxy.Error = new InvalidOperationException("transport down"); var service = Create(hub);
        if (user) await service.PublishToUserAsync(Guid.NewGuid(), Message); else await service.PublishToLockerAsync(Guid.NewGuid(), Message);
        Assert.Equal(1, hub.ClientsRecorder.Proxy.Calls);
    }
    private static SignalROperationsRealtimeNotifier Create(Hub hub) => new(hub, NullLogger<SignalROperationsRealtimeNotifier>.Instance);
    private static void AssertMessage(Hub hub, RealtimeEvent? message, CancellationToken cancellationToken)
    { var proxy = hub.ClientsRecorder.Proxy; Assert.Equal("operationUpdated", proxy.Method); Assert.Same(message, Assert.Single(proxy.Arguments!)); Assert.Equal(cancellationToken, proxy.Token); }
    private sealed class Proxy : IClientProxy
    {
        public int Calls { get; private set; }
        public string? Method { get; private set; }
        public object?[]? Arguments { get; private set; }
        public CancellationToken Token { get; private set; }
        public Exception? Error { get; set; }
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        { Calls++; Method = method; Arguments = args; Token = cancellationToken; return Error is null ? Task.CompletedTask : Task.FromException(Error); }
    }
    private sealed class RecordingClients : IHubClients
    {
        public Proxy Proxy { get; } = new();
        public string? GroupName { get; private set; }
        public IClientProxy Group(string groupName) { GroupName = groupName; return Proxy; }
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }
    private sealed class Hub : IHubContext<OperationsHub>
    {
        public RecordingClients ClientsRecorder { get; } = new();
        public IHubClients Clients => ClientsRecorder;
        public IGroupManager Groups => throw new NotSupportedException();
    }
}
