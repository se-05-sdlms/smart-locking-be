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
    [HttpGet("search")] public Task<IActionResult> Search([FromQuery] string? query, [FromQuery] Guid? lockerId, CancellationToken ct) => Execute((id, role) => service.SearchAsync(id, role, query, lockerId, ct));
    [HttpPost("emergency-unlocks")] public Task<IActionResult> Emergency(EmergencyUnlockRequest request, CancellationToken ct) => Execute((id, role) => service.EmergencyUnlockAsync(id, role, request, ct));
    [HttpPatch("lockers/{lockerId:guid}/status")] public Task<IActionResult> LockerStatus(Guid lockerId, UpdateOperationalStatusRequest request, CancellationToken ct) => Execute(async (id, role) => { await service.UpdateLockerStatusAsync(id, role, lockerId, request, ct); return new { success = true }; });
    [HttpPatch("lockers/{lockerId:guid}/compartments/{compartmentId:guid}/status")] public Task<IActionResult> CompartmentStatus(Guid lockerId, Guid compartmentId, UpdateOperationalStatusRequest request, CancellationToken ct) => Execute(async (id, role) => { await service.UpdateCompartmentStatusAsync(id, role, lockerId, compartmentId, request, ct); return new { success = true }; });
    [HttpGet("maintenance")] public Task<IActionResult> Maintenance(CancellationToken ct) => Execute((id, role) => service.GetMaintenanceAsync(id, role, ct));
    [HttpPost("maintenance")] public Task<IActionResult> CreateMaintenance(CreateMaintenanceRequest request, CancellationToken ct) => Execute((id, role) => service.CreateMaintenanceAsync(id, role, request, ct));
    [HttpPatch("maintenance/{id:guid}")] public Task<IActionResult> UpdateMaintenance(Guid id, UpdateMaintenanceRequest request, CancellationToken ct) => Execute((user, role) => service.UpdateMaintenanceAsync(user, role, id, request, ct));
    [HttpPost("overdue/{parcelId:guid}/transfer")] public Task<IActionResult> Transfer(Guid parcelId, CancellationToken ct) => Execute((id, role) => service.TransferOverdueAsync(id, role, parcelId, ct));

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
        try { return Ok(await service.GetReportAsync(start, end, ct)); } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
    [HttpGet("export.csv")] public async Task<IActionResult> Export([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        DateTimeOffset end = to ?? DateTimeOffset.UtcNow; DateTimeOffset start = from ?? end.AddDays(-30);
        OperationsReportResponse r = await service.GetReportAsync(start, end, ct);
        string csv = "From,To,Deliveries,Returns,RetrievedParcels,OpenIncidents,MaintenanceRequests,EmergencyUnlocks\n" + $"{r.From:O},{r.To:O},{r.Deliveries},{r.Returns},{r.RetrievedParcels},{r.OpenIncidents},{r.MaintenanceRequests},{r.EmergencyUnlocks}\n";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"boxora-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}

[ApiController, Route("api/audit-logs"), Authorize(Roles = "Administrator")]
public sealed class AuditLogsController(IOperationsService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Get([FromQuery] string? query, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct) => Ok(await service.GetAuditLogsAsync(query, from, to, ct));
}
