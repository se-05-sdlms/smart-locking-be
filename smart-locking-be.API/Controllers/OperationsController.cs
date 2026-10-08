using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/operations"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class OperationsController(IOperationsService service) : ControllerBase
{
    [HttpGet("lockers")] public Task<IActionResult> Lockers(CancellationToken ct) => Execute((id, role) => service.GetLockersAsync(id, role, ct));

    private async Task<IActionResult> Execute<T>(Func<Guid, string, Task<T>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id)) return Unauthorized();
        string role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        try { return Ok(await action(id, role)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}

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
    [HttpGet] public async Task<IActionResult> Get([FromQuery] string? query, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct) => Ok(await service.GetAuditLogsAsync(query, from, to, ct));
}
