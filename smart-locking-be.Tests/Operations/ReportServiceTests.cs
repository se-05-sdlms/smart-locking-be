using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Operations;

public sealed class ReportServiceTests
{
    [Fact]
    public async Task Summary_uses_half_open_date_range_and_validates_locker()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(-1), end = start.AddDays(1);
        db.ReturnRequests.AddRange(new ReturnRequest { Id = Guid.NewGuid(), ReturnCode = "123456", CreatedAt = start }, new ReturnRequest { Id = Guid.NewGuid(), ReturnCode = "234567", CreatedAt = end });
        await db.SaveChangesAsync();
        var service = new ReportService(db);
        var report = await service.GetSummaryAsync(start, end);
        Assert.Equal(1, report.Returns);
        Assert.Equal(0, report.Deliveries);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetSummaryAsync(end, start));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetSummaryAsync(start, end, Guid.NewGuid()));
    }
}
