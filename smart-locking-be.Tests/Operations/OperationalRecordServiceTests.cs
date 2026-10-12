using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Operations;

public sealed class OperationalRecordServiceTests
{
    [Fact]
    public async Task Search_denies_unassigned_locker_and_returns_full_total_before_pagination()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        for (int i = 0; i < 55; i++)
            db.Lockers.Add(new Locker { Id = Guid.NewGuid(), Code = $"L{i}", Address = "A", RecoveryAddress = "B", DeviceIdentifier = $"TEST{i}" });
        await db.SaveChangesAsync();
        var service = new OperationalRecordService(db);
        var page = await service.SearchAsync(Guid.NewGuid(), "Administrator", null, null, "Locker", null, null, pageNumber: 3, pageSize: 20);
        Assert.Equal(55, page.TotalCount);
        Assert.Equal(15, page.Items.Count);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchAsync(Guid.NewGuid(), "LockerOperator", null, db.Lockers.First().Id, null, null, null));
        Assert.Empty((await service.SearchAsync(Guid.NewGuid(), "LockerOperator", null, null, null, null, null)).Items);
    }
}
