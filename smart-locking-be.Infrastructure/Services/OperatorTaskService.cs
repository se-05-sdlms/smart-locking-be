using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class OperatorTaskService(ApplicationDbContext db, TimeProvider clock, IOptions<IncidentSlaOptions> sla) : IOperatorTaskService
{
    public async Task<OperatorTaskPage> ListAsync(Guid userId, string role, int offset, int limit, Guid? lockerId, CancellationToken ct)
    {
        if (offset is < 0 or > 10000 || limit is < 1 or > 100) throw BadRequestException.For("pagination", "Offset must be 0–10000 and limit 1–100.");
        DateTimeOffset now = clock.GetUtcNow();
        var permitted = db.EffectiveOperatorAssignments(now).Where(x => x.OperatorUserId == userId).Select(x => x.LockerId);
        var lockers = db.Lockers.AsNoTracking().Where(x => role == nameof(UserRole.Administrator) || permitted.Contains(x.Id));
        if (lockerId.HasValue) lockers = lockers.Where(x => x.Id == lockerId);
        var ids = lockers.Select(x => x.Id);
        int fetch = offset + limit + 1;
        var policy = await db.SystemPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && x.EffectiveFrom <= now && (x.EffectiveTo == null || now < x.EffectiveTo), ct);
        var incidents = await db.Incidents.AsNoTracking().Where(x => x.LockerId.HasValue && ids.Contains(x.LockerId.Value) && x.Status != IncidentStatus.Resolved)
            .OrderBy(x => x.Type.ToLower() == "locker" ? 0 : x.Type.ToLower() == "compartment" || x.Type.ToLower() == "payment" || x.Type.ToLower() == "retrieval" ? 1 : x.Type.ToLower() == "other" ? 3 : 2)
            .ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new { x.Id, LockerId = x.LockerId!.Value, LockerCode = x.Locker!.Code, x.Title, x.Type, x.Status, x.CreatedAt, x.AssignedOperatorUserId }).Take(fetch).ToListAsync(ct);
        var maintenance = await db.MaintenanceRequests.AsNoTracking().Where(x => ids.Contains(x.LockerId) && (x.Status == MaintenanceStatus.Open || x.Status == MaintenanceStatus.InProgress))
            .OrderBy(x => x.Priority == MaintenancePriority.Critical ? 0 : x.Priority == MaintenancePriority.High ? 1 : x.Priority == MaintenancePriority.Normal ? 2 : 3).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.LockerId, LockerCode = x.Locker.Code, x.Description, x.Priority, x.Status, x.CreatedAt }).Take(fetch).ToListAsync(ct);
        var parcels = await db.Parcels.AsNoTracking().Where(x => ids.Contains(x.DeliveryRequest.LockerId) && (x.Status == ParcelStatus.Stored || x.Status == ParcelStatus.Overdue) && x.MaxStorageUntil <= now)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new { x.Id, LockerId = x.DeliveryRequest.LockerId, LockerCode = x.DeliveryRequest.Locker.Code, x.ParcelCode, x.Status, x.MaxStorageUntil, x.CreatedAt }).Take(fetch).ToListAsync(ct);
        var items = incidents.Select(x =>
        {
            IncidentPriority priority = IncidentSlaService.GetPriority(x.Type);
            SlaTargets targets = IncidentSlaService.TargetsFor(priority, policy, sla.Value);
            int minutes = x.Status == IncidentStatus.Open && x.AssignedOperatorUserId == null ? targets.AcknowledgeMinutes : targets.ResolveMinutes;
            DateTimeOffset? due = sla.Value.Enabled ? x.CreatedAt.AddMinutes(minutes) : null;
            return new OperatorTaskResponse("incident", x.Id, x.LockerId, x.LockerCode, x.Title, priority.ToString(), x.Status.ToString(), due, x.CreatedAt, $"/api/incidents/{x.Id}");
        }).Concat(maintenance.Select(x => new OperatorTaskResponse("maintenance", x.Id, x.LockerId, x.LockerCode, x.Description,
            x.Priority == MaintenancePriority.Normal ? "Medium" : x.Priority.ToString(), x.Status.ToString(), null, x.CreatedAt, $"/api/maintenance-requests/{x.Id}")))
        .Concat(parcels.Select(x => new OperatorTaskResponse("recovery", x.Id, x.LockerId, x.LockerCode, $"Chuyển kiện {x.ParcelCode} tới địa chỉ thu hồi của tủ", "High", x.Status.ToString(), x.MaxStorageUntil, x.CreatedAt, $"/api/parcels/{x.Id}")))
        .OrderBy(x => Rank(x.Priority)).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id).ThenBy(x => x.Kind, StringComparer.Ordinal).ToList();
        return new(items.Skip(offset).Take(limit).ToList(), offset, limit, items.Count > offset + limit);
    }

    public async Task<IReadOnlyList<TaskHandoverResponse>> HistoryAsync(Guid userId, string role, string kind, Guid taskId, CancellationToken ct)
    {
        var task = await ResolveAsync(kind, taskId, ct);
        await DemandAsync(userId, role, task.LockerId, ct);
        var entries = await db.Set<OperatorTaskHandover>().AsNoTracking().Where(x => x.TaskKind == kind && x.TaskId == taskId).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        var entryIds = entries.Select(x => x.Id).ToList();
        var acknowledgements = await db.Set<OperatorTaskAcknowledgement>().AsNoTracking().Where(x => entryIds.Contains(x.HandoverId)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        return entries.Select(x => Map(x, acknowledgements.Where(a => a.HandoverId == x.Id).Select(Map).ToList())).ToList();
    }

    public async Task<TaskHandoverResponse> CreateHandoverAsync(Guid userId, string role, string kind, Guid taskId, CreateTaskHandoverRequest request, CancellationToken ct)
    {
        string done = Text(request.WorkDone, 2000, "workDone", true), blockers = Text(request.Blockers, 2000, "blockers", false), next = Text(request.NextSteps, 2000, "nextSteps", true);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var task = await ResolveAsync(kind, taskId, ct);
        await DemandAsync(userId, role, task.LockerId, ct);
        if (!task.Open) throw new ConflictException("TASK_CLOSED", "Only open work can be handed over.");
        if (request.IntendedReceiverUserId.HasValue && !await db.EffectiveOperatorAssignments(clock.GetUtcNow()).AnyAsync(x => x.LockerId == task.LockerId && x.OperatorUserId == request.IntendedReceiverUserId, ct))
            throw BadRequestException.For("intendedReceiverUserId", "Receiver must currently have effective operator permission for this locker.");
        var item = new OperatorTaskHandover
        {
            Id = Guid.NewGuid(),
            LockerId = task.LockerId,
            TaskKind = kind,
            TaskId = taskId,
            AuthorUserId = userId,
            AuthorName = await db.Users.Where(x => x.Id == userId).Select(x => x.ResidentProfile != null ? x.ResidentProfile.FullName : x.PhoneNumber ?? x.Email ?? x.Id.ToString()).SingleAsync(ct),
            IntendedReceiverUserId = request.IntendedReceiverUserId,
            WorkDone = done,
            Blockers = blockers,
            NextSteps = next,
            CreatedAt = clock.GetUtcNow()
        };
        db.Add(item);
        Audit(userId, "OperatorTask.HandoverCreated", item.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(item, []);
    }

    public async Task<TaskAcknowledgementResponse> AcknowledgeAsync(Guid userId, string role, Guid handoverId, AcknowledgeTaskHandoverRequest request, CancellationToken ct)
    {
        string notes = Text(request.Notes, 1000, "notes", false);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var handover = await db.Set<OperatorTaskHandover>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == handoverId, ct)
            ?? throw new NotFoundException("TASK_HANDOVER_NOT_FOUND", "Handover not found.");
        await DemandAsync(userId, role, handover.LockerId, ct);
        if (handover.AuthorUserId == userId) throw BadRequestException.For("receiver", "The receiving operator must acknowledge the handover.");
        var existing = await db.Set<OperatorTaskAcknowledgement>().AsNoTracking().SingleOrDefaultAsync(x => x.HandoverId == handoverId && x.ReceiverUserId == userId, ct);
        if (existing != null) { await transaction.CommitAsync(ct); return Map(existing); }
        var item = new OperatorTaskAcknowledgement
        {
            Id = Guid.NewGuid(),
            HandoverId = handoverId,
            ReceiverUserId = userId,
            ReceiverName = await db.Users.Where(x => x.Id == userId).Select(x => x.ResidentProfile != null ? x.ResidentProfile.FullName : x.PhoneNumber ?? x.Email ?? x.Id.ToString()).SingleAsync(ct),
            Notes = notes,
            CreatedAt = clock.GetUtcNow()
        };
        db.Add(item);
        Audit(userId, "OperatorTask.HandoverAcknowledged", handoverId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(item);
    }

    private async Task<(Guid LockerId, bool Open)> ResolveAsync(string kind, Guid taskId, CancellationToken ct)
    {
        if (kind == "incident")
        {
            var item = await db.Incidents.AsNoTracking().Where(x => x.Id == taskId && x.LockerId != null).Select(x => new { LockerId = x.LockerId!.Value, Open = x.Status != IncidentStatus.Resolved }).SingleOrDefaultAsync(ct);
            if (item != null) return (item.LockerId, item.Open);
        }
        else if (kind == "maintenance")
        {
            var item = await db.MaintenanceRequests.AsNoTracking().Where(x => x.Id == taskId).Select(x => new { x.LockerId, Open = x.Status == MaintenanceStatus.Open || x.Status == MaintenanceStatus.InProgress }).SingleOrDefaultAsync(ct);
            if (item != null) return (item.LockerId, item.Open);
        }
        else if (kind == "recovery")
        {
            DateTimeOffset now = clock.GetUtcNow();
            var item = await db.Parcels.AsNoTracking().Where(x => x.Id == taskId).Select(x => new { x.DeliveryRequest.LockerId, Open = (x.Status == ParcelStatus.Stored || x.Status == ParcelStatus.Overdue) && x.MaxStorageUntil <= now }).SingleOrDefaultAsync(ct);
            if (item != null) return (item.LockerId, item.Open);
        }
        else throw BadRequestException.For("kind", "Use incident, maintenance or recovery.");
        throw new NotFoundException("OPERATOR_TASK_NOT_FOUND", "Source work record not found.");
    }
    private async Task DemandAsync(Guid userId, string role, Guid lockerId, CancellationToken ct)
    {
        if (role == nameof(UserRole.Administrator)) return;
        if (role != nameof(UserRole.LockerOperator) || !await db.EffectiveOperatorAssignments(clock.GetUtcNow()).AnyAsync(x => x.OperatorUserId == userId && x.LockerId == lockerId, ct))
            throw new ForbiddenException(LockerErrorCodes.AccessDenied, "Current effective operator permission is required.");
    }
    private async Task LockAsync(CancellationToken ct)
    {
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(19520261028)", ct);
    }
    private static int Rank(string priority) => priority switch { "Critical" => 0, "High" => 1, "Medium" => 2, _ => 3 };
    private static string Text(string? value, int max, string field, bool required)
    {
        string text = value?.Trim() ?? string.Empty;
        if (text.Length > max || required && text.Length == 0) throw BadRequestException.For(field, $"{(required ? "Required, " : string.Empty)}maximum {max} characters.");
        return text;
    }
    private static TaskAcknowledgementResponse Map(OperatorTaskAcknowledgement x) => new(x.Id, x.ReceiverUserId, x.ReceiverName, x.Notes, x.CreatedAt);
    private static TaskHandoverResponse Map(OperatorTaskHandover x, IReadOnlyList<TaskAcknowledgementResponse> acknowledgements) =>
        new(x.Id, x.TaskKind, x.TaskId, x.LockerId, x.AuthorUserId, x.AuthorName, x.IntendedReceiverUserId, x.WorkDone, x.Blockers, x.NextSteps, x.CreatedAt, acknowledgements);
    private void Audit(Guid userId, string action, Guid id) => db.AuditLogs.Add(new AuditLog
    {
        Id = Guid.NewGuid(),
        ActorUserId = userId,
        Action = action,
        EntityType = "OperatorTaskHandover",
        EntityId = id,
        Result = AuditLogResult.Succeeded,
        OccurredAt = clock.GetUtcNow()
    });
}
