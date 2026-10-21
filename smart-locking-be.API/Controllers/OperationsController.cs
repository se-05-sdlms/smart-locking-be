using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/operations"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class OperationsController(IOperationsService service) : ControllerBase
{
    private Guid Actor => User.GetRequiredIdentity().UserId;
    private string Role => User.GetRequiredIdentity().Role;

    [HttpGet("lockers")]
    public async Task<IActionResult> Lockers(CancellationToken ct) => Ok(await service.GetLockersAsync(Actor, Role, ct));
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? query, [FromQuery] Guid? lockerId, CancellationToken ct)
        => Ok(await service.SearchAsync(Actor, Role, query, lockerId, ct));
    [HttpPost("emergency-unlocks")]
    public async Task<IActionResult> Emergency(EmergencyUnlockRequest request, CancellationToken ct)
        => Ok(await service.EmergencyUnlockAsync(Actor, Role, request, ct));
    [HttpPatch("lockers/{lockerId:guid}/status")]
    public async Task<IActionResult> LockerStatus(Guid lockerId, UpdateOperationalStatusRequest request, CancellationToken ct)
    {
        await service.UpdateLockerStatusAsync(Actor, Role, lockerId, request, ct);
        return Ok(new { success = true });
    }
    [HttpPatch("lockers/{lockerId:guid}/compartments/{compartmentId:guid}/status")]
    public async Task<IActionResult> CompartmentStatus(Guid lockerId, Guid compartmentId, UpdateOperationalStatusRequest request, CancellationToken ct)
    {
        await service.UpdateCompartmentStatusAsync(Actor, Role, lockerId, compartmentId, request, ct);
        return Ok(new { success = true });
    }
    [HttpGet("maintenance")]
    public async Task<IActionResult> Maintenance(CancellationToken ct) => Ok(await service.GetMaintenanceAsync(Actor, Role, ct));
    [HttpPost("maintenance")]
    public async Task<IActionResult> CreateMaintenance(CreateMaintenanceRequest request, CancellationToken ct)
        => Ok(await service.CreateMaintenanceAsync(Actor, Role, request, ct));
    [HttpPatch("maintenance/{id:guid}")]
    public async Task<IActionResult> UpdateMaintenance(Guid id, UpdateMaintenanceRequest request, CancellationToken ct)
        => Ok(await service.UpdateMaintenanceAsync(Actor, Role, id, request, ct));

    // Gọi sau khi nhân viên đã lấy hàng bằng luồng mở vận hành riêng.
    // 202 nghĩa còn chờ ESP thu hồi PIN; chưa báo hàng đã chuyển hoặc ngăn đã trống.
    [HttpPost("overdue/{parcelId:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid parcelId, CancellationToken ct)
    {
        var response = await service.TransferOverdueAsync(Actor, Role, parcelId, ct);
        return response.ResetStatus == "Pending" ? Accepted(response) : Ok(response);
    }

}

[ApiController, Route("api/operations/reports"), Authorize(Roles = "Administrator")]
public sealed class LegacyOperationsReportsController(IOperationsService service) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        DateTimeOffset end = to ?? DateTimeOffset.UtcNow; DateTimeOffset start = from ?? end.AddDays(-30);
        return Ok(await service.GetReportAsync(start, end, ct));
    }
    [HttpGet("export.csv")]
    public async Task<IActionResult> Export([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        DateTimeOffset end = to ?? DateTimeOffset.UtcNow; DateTimeOffset start = from ?? end.AddDays(-30);
        OperationsReportResponse r = await service.GetReportAsync(start, end, ct);
        string csv = "From,To,Deliveries,Returns,RetrievedParcels,OpenIncidents,MaintenanceRequests,EmergencyUnlocks\n" + $"{r.From:O},{r.To:O},{r.Deliveries},{r.Returns},{r.RetrievedParcels},{r.OpenIncidents},{r.MaintenanceRequests},{r.EmergencyUnlocks}\n";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"boxora-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}

[ApiController, Route("api/operations/audit-logs"), Authorize(Roles = "Administrator")]
public sealed class LegacyOperationsAuditLogsController(IOperationsService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Get([FromQuery] string? query, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct) => Ok(await service.GetAuditLogsAsync(query, from, to, ct));
}
