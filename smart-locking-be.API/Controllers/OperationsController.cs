using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/reports"), Authorize(Roles = "Administrator")]
public sealed class ReportsController(IOperationsService service) : ControllerBase
{
    [HttpGet("summary")] public async Task<IActionResult> Summary([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        DateTimeOffset end = to ?? DateTimeOffset.UtcNow; DateTimeOffset start = from ?? end.AddDays(-30);
        try
        {
            OperationsReportResponse report = await service.GetReportAsync(start, end, ct);
            if (!Request.Headers.Accept.Any(value => value?.Contains("text/csv", StringComparison.OrdinalIgnoreCase) == true)) return Ok(report);
            string csv = "From,To,Deliveries,Returns,RetrievedParcels,OpenIncidents,MaintenanceRequests,EmergencyUnlocks\n" + $"{report.From:O},{report.To:O},{report.Deliveries},{report.Returns},{report.RetrievedParcels},{report.OpenIncidents},{report.MaintenanceRequests},{report.EmergencyUnlocks}\n";
            return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"boxora-report-{DateTime.UtcNow:yyyyMMdd}.csv");
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
}

[ApiController, Route("api/audit-logs"), Authorize(Roles = "Administrator")]
public sealed class AuditLogsController(IOperationsService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Get([FromQuery] string? query, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await service.GetAuditLogsAsync(query, from, to, ct, pageNumber, pageSize));
}
