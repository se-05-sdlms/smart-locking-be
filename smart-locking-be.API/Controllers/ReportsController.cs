using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Interfaces.Services;
using System.Text;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/reports"), Authorize(Roles = "Administrator")]
public sealed class ReportsController(IReportService service, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? lockerId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset end = to ?? timeProvider.GetUtcNow();
        DateTimeOffset start = from ?? end.AddDays(-30);
        OperationsReportResponse report = await service.GetSummaryAsync(start, end, lockerId, cancellationToken);
        if (!Request.Headers.Accept.Any(value => value?.Contains("text/csv", StringComparison.OrdinalIgnoreCase) == true))
            return Ok(report);
        string csv = "From,To,Deliveries,Returns,RetrievedParcels,OpenIncidents,MaintenanceRequests,EmergencyUnlocks\n" +
            $"{report.From:O},{report.To:O},{report.Deliveries},{report.Returns},{report.RetrievedParcels},{report.OpenIncidents},{report.MaintenanceRequests},{report.EmergencyUnlocks}\n";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"boxora-report-{end:yyyyMMdd}.csv");
    }

    /// <summary>
    /// Báo cáo chi tiết (UC-27). type: parcels, returns, locker-utilization, incidents, maintenance,
    /// overdue-transfers, overdue-fees, payments, operational-activities. Thời gian tính theo UTC.
    /// </summary>
    [HttpGet("{type}")]
    public async Task<IActionResult> Detail(
        string type,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? lockerId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetReportAsync(BuildQuery(type, from, to, lockerId, status, page, pageSize), cancellationToken));

    /// <summary>Xuất CSV đúng dữ liệu đang lọc (không phân trang).</summary>
    [HttpGet("{type}/export")]
    public async Task<IActionResult> Export(
        string type,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? lockerId,
        [FromQuery] string? status,
        CancellationToken cancellationToken = default)
    {
        ReportFile file = await service.ExportCsvAsync(BuildQuery(type, from, to, lockerId, status, 1, 1), cancellationToken);
        if (file.Truncated) Response.Headers["X-Report-Truncated"] = "true";
        return File(file.Content, file.ContentType, file.FileName);
    }

    private ReportQuery BuildQuery(string type, DateTimeOffset? from, DateTimeOffset? to, Guid? lockerId, string? status, int page, int pageSize)
    {
        if (!Enum.TryParse(type.Replace("-", string.Empty), ignoreCase: true, out ReportType reportType) || !Enum.IsDefined(reportType))
            throw BadRequestException.For("type", "Report type is invalid.");
        DateTimeOffset end = to ?? timeProvider.GetUtcNow();
        return new ReportQuery(reportType, from ?? end.AddDays(-30), end, lockerId, status, page, pageSize);
    }
}
