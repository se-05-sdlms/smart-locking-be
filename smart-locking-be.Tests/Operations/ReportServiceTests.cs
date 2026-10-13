using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Operations;

public sealed class ReportServiceTests
{
    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    #region RPT-01: GetSummaryAsync
    [Fact]
    [Trait("Category", "RPT-01")]
    public async Task GetSummaryAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new ReportService(dbContext);
        DateTimeOffset from = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        OperationsReportResponse report = await service.GetSummaryAsync(from, to, null, default);

        Assert.Equal(from, report.From);
        Assert.Equal(to, report.To);
        Assert.Equal(0, report.Deliveries);
    }

    [Fact]
    [Trait("Category", "RPT-01")]
    public async Task GetSummaryAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LK-RPT-01",
            Address = "Address",
            RecoveryAddress = "Recovery",
            DeviceIdentifier = "DEV-RPT-01",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        var service = new ReportService(dbContext);
        DateTimeOffset from = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        OperationsReportResponse report = await service.GetSummaryAsync(from, to, locker.Id, default);

        Assert.Equal(from, report.From);
        Assert.Equal(to, report.To);
    }

    [Fact]
    [Trait("Category", "RPT-01")]
    public async Task GetSummaryAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new ReportService(dbContext);
        DateTimeOffset time = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetSummaryAsync(time, time, null, default));

        Assert.Equal("Khoảng thời gian báo cáo không hợp lệ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "RPT-01")]
    public async Task GetSummaryAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new ReportService(dbContext);
        DateTimeOffset from = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetSummaryAsync(from, to, null, default));

        Assert.Equal("Khoảng thời gian báo cáo không hợp lệ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "RPT-01")]
    public async Task GetSummaryAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new ReportService(dbContext);
        DateTimeOffset from = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetSummaryAsync(from, to, Guid.NewGuid(), default));

        Assert.Equal("Không tìm thấy locker.", ex.Message);
    }
    #endregion
}
