using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Controllers;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.Tests.API;

public sealed class ReportBoundaryTests
{
    [Theory]
    [InlineData("summary")]
    [InlineData("detail")]
    [InlineData("export")]
    public async Task DateOnlyUsesVietnamMidnightForEveryEndpoint(string endpoint)
    {
        var service = new CapturingService();
        var controller = new ReportsController(service, TimeProvider.System)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.QueryString = new QueryString("?from=2026-11-16&to=2026-11-17");
        // Simulate a host whose binder interpreted dates in a different timezone.
        var boundFrom = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(-5));
        var boundTo = boundFrom.AddDays(1);
        await Invoke(controller, endpoint, boundFrom, boundTo);
        Assert.Equal(DateTimeOffset.Parse("2026-11-15T17:00:00Z"), service.From);
        Assert.Equal(DateTimeOffset.Parse("2026-11-16T17:00:00Z"), service.To);
        Assert.Equal(TimeSpan.Zero, service.From.Offset);
        Assert.Equal(TimeSpan.Zero, service.To.Offset);
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("detail")]
    [InlineData("export")]
    public async Task ExplicitOffsetsPreserveInstant(string endpoint)
    {
        var service = new CapturingService();
        var controller = new ReportsController(service, TimeProvider.System)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.QueryString = new QueryString("?from=2026-11-16T03:00:00%2B07:00&to=2026-11-17T03:00:00%2B07:00");
        var from = DateTimeOffset.Parse("2026-11-16T03:00:00+07:00");
        await Invoke(controller, endpoint, from, from.AddDays(1));
        Assert.Equal(from.UtcDateTime, service.From.UtcDateTime);
        Assert.Equal(from.AddDays(1).UtcDateTime, service.To.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, service.From.Offset);
        Assert.Equal(TimeSpan.Zero, service.To.Offset);
    }

    private static Task<IActionResult> Invoke(ReportsController controller, string endpoint, DateTimeOffset from, DateTimeOffset to) => endpoint switch
    {
        "summary" => controller.Summary(from, to, null, default),
        "detail" => controller.Detail("operational-activities", from, to, null, null),
        _ => controller.Export("operational-activities", from, to, null, null)
    };

    [Fact]
    public async Task DateOutsideUtcRange_IsRejectedAsBadRequest()
    {
        var controller = new ReportsController(new CapturingService(), TimeProvider.System)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.QueryString = new QueryString("?from=0001-01-01");
        await Assert.ThrowsAsync<smart_locking_be.Application.Exceptions.BadRequestException>(() =>
            controller.Summary(DateTimeOffset.MinValue, null, null, default));
    }

    internal sealed class CapturingService : IReportService
    {
        public DateTimeOffset From { get; private set; }
        public DateTimeOffset To { get; private set; }

        public Task<OperationsReportResponse> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, Guid? lockerId = null, CancellationToken cancellationToken = default)
        {
            From = from;
            To = to;
            return Task.FromResult(new OperationsReportResponse(from, to, 0, 0, 0, 0, 0, 0));
        }

        public Task<ReportResponse> GetReportAsync(ReportQuery query, CancellationToken cancellationToken = default)
        {
            From = query.From;
            To = query.To;
            return Task.FromResult(new ReportResponse(query.Type.ToString(), From, To, "UTC", From, [], [], 0, 1, 50, new Dictionary<string, int>(), null));
        }

        public Task<ReportFile> ExportCsvAsync(ReportQuery query, CancellationToken cancellationToken = default)
        {
            From = query.From;
            To = query.To;
            return Task.FromResult(new ReportFile("report.csv", "text/csv", [], false));
        }
    }
}
