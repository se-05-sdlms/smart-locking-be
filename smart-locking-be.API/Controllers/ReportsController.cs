using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;

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
}
