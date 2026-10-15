using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Operations;

public sealed class ReportServiceTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
    private static readonly DateTimeOffset To = From.AddDays(1);
    // Return aggregates are intentionally outside the authorized scope; no Return/Payment/OCR data/assertions.
    [Fact]
    [Trait("Category", "RPT-01")]
    public async Task GetSummaryAsync_UTC01()
    {
        await using var c = new Context(); c.Seed(c.Locker, From); c.Seed(c.Locker, To); c.Seed(c.Locker, From.AddTicks(-1)); c.Seed(c.OtherLocker, From); await c.Db.SaveChangesAsync();
        var result = await c.Service.GetSummaryAsync(From, To, c.Locker.Id);
        Assert.Equal(From, result.From); Assert.Equal(To, result.To);
        Assert.Equal(1, result.Deliveries); Assert.Equal(1, result.RetrievedParcels); Assert.Equal(1, result.MaintenanceRequests); Assert.Equal(1, result.EmergencyUnlocks);
        // Open incidents are backlog before To, including incidents created before From.
        Assert.Equal(2, result.OpenIncidents); Assert.Empty(c.Db.AuditLogs);
    }
    [Fact]
    [Trait("Category", "RPT-01")]
    public async Task GetSummaryAsync_UTC02()
    { await using var c = new Context(); var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.GetSummaryAsync(To, From)); Assert.Equal("Khoảng thời gian báo cáo không hợp lệ.", ex.Message); }
    [Fact]
    [Trait("Category", "RPT-01")]
    public async Task GetSummaryAsync_UTC03()
    { await using var c = new Context(); var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.GetSummaryAsync(From, To, Guid.NewGuid())); Assert.Equal("Không tìm thấy locker.", ex.Message); }
    [Fact(Skip = "Excel RPT-01 UTC04 passes null dates; dev accepts non-nullable DateTimeOffset and no default/null behavior is specified.")]
    [Trait("Category", "RPT-01")]
    public Task GetSummaryAsync_UTC04() => Task.CompletedTask;
    [Fact]
    [Trait("Category", "RPT-01")]
    public async Task GetSummaryAsync_NoLockerFilterIncludesAllLockersAndExcludesResolvedIncidents()
    {
        await using var c = new Context(); c.Seed(c.Locker, From); c.Seed(c.OtherLocker, From); c.Db.Incidents.Add(new Incident { Id = Guid.NewGuid(), LockerId = c.Locker.Id, Status = IncidentStatus.Resolved, Type = "Locker", Title = "Resolved", Description = "Fixed", CreatedAt = From }); await c.Db.SaveChangesAsync();
        var result = await c.Service.GetSummaryAsync(From, To); Assert.Equal(2, result.Deliveries); Assert.Equal(2, result.RetrievedParcels); Assert.Equal(2, result.MaintenanceRequests); Assert.Equal(2, result.EmergencyUnlocks); Assert.Equal(2, result.OpenIncidents);
    }
    [Fact]
    [Trait("Category", "RPT-01")]
    public async Task GetSummaryAsync_EmptyRangeIsRejectedAndEmptyDatasetHasZeroCounts()
    {
        await using var c = new Context(); await Assert.ThrowsAsync<ArgumentException>(() => c.Service.GetSummaryAsync(From, From));
        var result = await c.Service.GetSummaryAsync(From, To); Assert.Equal(0, result.Deliveries); Assert.Equal(0, result.RetrievedParcels); Assert.Equal(0, result.MaintenanceRequests); Assert.Equal(0, result.OpenIncidents); Assert.Equal(0, result.EmergencyUnlocks);
    }
    private sealed class Context : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Locker Locker { get; } = new() { Id = Guid.NewGuid(), Code = "L01", Address = "Address", DeviceIdentifier = "D01" };
        public Locker OtherLocker { get; } = new() { Id = Guid.NewGuid(), Code = "L02", Address = "Address", DeviceIdentifier = "D02" };
        private readonly SystemPolicy policy = new() { Id = Guid.NewGuid(), Version = 1, Currency = "VND" };
        public ReportService Service => new(Db);
        public Context() { Db.AddRange(Locker, OtherLocker, policy); Db.SaveChanges(); }
        public void Seed(Locker locker, DateTimeOffset at)
        {
            var delivery = new DeliveryRequest { Id = Guid.NewGuid(), Locker = locker, SystemPolicy = policy, GuestSessionTokenHash = "hash", CreatedAt = at };
            Db.AddRange(delivery, new Parcel { Id = Guid.NewGuid(), DeliveryRequest = delivery, ParcelCode = Guid.NewGuid().ToString(), RetrievedAt = at }, new Incident { Id = Guid.NewGuid(), LockerId = locker.Id, Status = IncidentStatus.Open, Type = "Locker", Title = "Door", Description = "Stuck", CreatedAt = at }, new MaintenanceRequest { Id = Guid.NewGuid(), LockerId = locker.Id, Description = "Repair", CreatedAt = at }, new EmergencyUnlock { Id = Guid.NewGuid(), LockerId = locker.Id, Reason = "Emergency", RequestedAt = at });
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
