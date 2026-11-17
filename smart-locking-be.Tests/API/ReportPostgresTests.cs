using Microsoft.EntityFrameworkCore;
using Npgsql;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using System.Text;

namespace smart_locking_be.Tests.API;

public sealed class ReportPostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_report_test_{Guid.NewGuid():N}";
    private string adminConnectionString = string.Empty;
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        string? configured = Environment.GetEnvironmentVariable("SMART_LOCKING_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var builder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        adminConnectionString = builder.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await create.ExecuteNonQueryAsync();
        builder.Database = databaseName;
        connectionString = builder.ConnectionString;
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(adminConnectionString)) return;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [ReportPostgresFact]
    public async Task OffsetBounds_AllReportTypesSummaryAndExports_AreAcceptedByNpgsql()
    {
        await using var db = Context();
        var service = new ReportService(db, TimeProvider.System);
        var from = DateTimeOffset.Parse("2026-11-16T00:00:00+07:00");
        var to = from.AddDays(1);
        var summary = await service.GetSummaryAsync(from, to);
        Assert.Equal(TimeSpan.Zero, summary.From.Offset);
        Assert.Equal(from.UtcDateTime, summary.From.UtcDateTime);
        foreach (ReportType type in Enum.GetValues<ReportType>())
        {
            var query = new ReportQuery(type, from, to, null, null, 1, 50);
            ReportResponse report = await service.GetReportAsync(query);
            Assert.Equal(TimeSpan.Zero, report.From.Offset);
            Assert.Equal(TimeSpan.Zero, report.To.Offset);
            Assert.Equal(from.UtcDateTime, report.From.UtcDateTime);
            Assert.Equal(to.UtcDateTime, report.To.UtcDateTime);
            Assert.Equal(0, report.TotalCount);
            Assert.NotEmpty((await service.ExportCsvAsync(query)).Content);
        }
    }

    [ReportPostgresFact]
    public async Task DetailAndCsv_IncludeStartExcludeEnd_AndPreserveSameInstantAcrossOffsets()
    {
        await using var db = Context();
        var from = DateTimeOffset.Parse("2026-11-16T00:00:00+07:00");
        var to = from.AddDays(1);
        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "REPORT-TEST",
            DeviceIdentifier = "REPORT-TEST",
            Address = "Test",
            RecoveryAddress = "Test",
            CreatedAt = from.UtcDateTime,
            UpdatedAt = from.UtcDateTime
        };
        db.Add(locker);
        (DateTimeOffset At, string Detail)[] events =
        [
            (from.AddTicks(-10), "BEFORE-START"), (from, "AT-START"),
            (to.AddTicks(-10), "BEFORE-END"), (to, "AT-END")
        ];
        foreach (var entry in events)
            db.Add(new LockerEvent
            {
                Id = Guid.NewGuid(),
                Locker = locker,
                Details = entry.Detail,
                OccurredAt = entry.At.ToUniversalTime(),
                ReceivedAt = entry.At.ToUniversalTime()
            });
        await db.SaveChangesAsync();
        var service = new ReportService(db, TimeProvider.System);
        var query = new ReportQuery(ReportType.OperationalActivities, from, to, locker.Id, null, 1, 50);
        ReportResponse offsetReport = await service.GetReportAsync(query);
        ReportResponse utcReport = await service.GetReportAsync(query with { From = from.ToUniversalTime(), To = to.ToUniversalTime() });
        Assert.Equal(2, offsetReport.TotalCount);
        Assert.Equal(offsetReport.Rows.Select(row => row["details"]), utcReport.Rows.Select(row => row["details"]));
        string csv = Encoding.UTF8.GetString((await service.ExportCsvAsync(query)).Content);
        Assert.Contains("AT-START", csv);
        Assert.Contains("BEFORE-END", csv);
        Assert.DoesNotContain("BEFORE-START", csv);
        Assert.DoesNotContain("AT-END", csv);
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
}

public sealed class ReportPostgresFactAttribute : FactAttribute
{
    public ReportPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMART_LOCKING_TEST_POSTGRES")))
            Skip = "Set SMART_LOCKING_TEST_POSTGRES to an account allowed to create disposable test databases. CI always sets it.";
    }
}
