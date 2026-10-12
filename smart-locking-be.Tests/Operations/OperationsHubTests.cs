using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.API.Hubs;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Tests.Operations;

public sealed class OperationsHubTests
{
    [Fact]
    public async Task Operator_can_subscribe_only_to_an_assigned_locker()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Guid user = Guid.NewGuid(), locker = Guid.NewGuid();
        var groups = new Groups();
        var hub = new OperationsHub(db) { Context = new Caller(user), Groups = groups };
        await Assert.ThrowsAsync<HubException>(() => hub.SubscribeLocker(locker));
        Assert.Empty(groups.Joined);
        db.OperatorAssignments.Add(new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = user, LockerId = locker, AssignedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await hub.SubscribeLocker(locker);
        Assert.Equal(OperationsHub.LockerGroup(locker), Assert.Single(groups.Joined));
        await Assert.ThrowsAsync<HubException>(() => hub.SubscribeLocker(Guid.NewGuid()));
    }

    private sealed class Groups : IGroupManager
    {
        public List<string> Joined { get; } = [];
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) { Joined.Add(groupName); return Task.CompletedTask; }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Caller(Guid user) : HubCallerContext
    {
        public override string ConnectionId => "test";
        public override string UserIdentifier => user.ToString();
        public override ClaimsPrincipal User => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim(ClaimTypes.Role, "LockerOperator")], "test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
