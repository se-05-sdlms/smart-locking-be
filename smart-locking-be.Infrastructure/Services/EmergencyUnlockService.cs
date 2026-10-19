using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class EmergencyUnlockService(
    ApplicationDbContext dbContext,
    ILockerAccessService lockerAccessService,
    TimeProvider timeProvider) : IEmergencyUnlockService
{
    public async Task<EmergencyUnlockResponse> CreateAsync(
        Guid userId,
        string role,
        EmergencyUnlockRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10)
            throw BadRequestException.For("reason", "Emergency unlock reason must contain at least 10 characters.");
        await EnsureAccessAsync(userId, role, request.LockerId, cancellationToken);
        LockerCompartment compartment = await dbContext.LockerCompartments.SingleOrDefaultAsync(
            item => item.Id == request.CompartmentId && item.LockerId == request.LockerId,
            cancellationToken) ?? throw new NotFoundException(LockerErrorCodes.InvalidCompartment, "Locker compartment not found.");
        if (request.IncidentId.HasValue && !await dbContext.Incidents.AnyAsync(incident =>
            incident.Id == request.IncidentId &&
            incident.LockerId == request.LockerId &&
            incident.LockerCompartmentId == request.CompartmentId,
            cancellationToken))
            throw new BadRequestException(IncidentErrorCodes.InvalidLink, "Incident does not match the locker or compartment being opened.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        EmergencyUnlock entity = new()
        {
            Id = Guid.NewGuid(),
            OperatorUserId = userId,
            LockerId = request.LockerId,
            LockerCompartmentId = compartment.Id,
            IncidentId = request.IncidentId,
            Reason = request.Reason.Trim(),
            Result = EmergencyUnlockResult.Pending,
            RequestedAt = now
        };
        dbContext.EmergencyUnlocks.Add(entity);
        OpenLockerResponse opened = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            request.LockerId, compartment.Id, userId, null, null, null,
            LockerAccessType.OperatorEmergency, LockerAccessMethod.OperatorAuthorization,
            null, "Operator Web"), cancellationToken);
        entity.Result = opened.Result == LockerAccessResult.Succeeded
            ? EmergencyUnlockResult.Succeeded
            : EmergencyUnlockResult.Failed;
        entity.CompletedAt = timeProvider.GetUtcNow();
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = userId,
            Action = "Locker.EmergencyUnlock",
            EntityType = nameof(LockerCompartment),
            EntityId = compartment.Id,
            Result = entity.Result == EmergencyUnlockResult.Succeeded
                ? AuditLogResult.Succeeded
                : AuditLogResult.Failed,
            Details = $"LockerId={request.LockerId}; Result={entity.Result}; Reason={entity.Reason}",
            OccurredAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new EmergencyUnlockResponse(entity.Id, entity.Result, compartment.Code, entity.RequestedAt);
    }

    private async Task EnsureAccessAsync(Guid userId, string role, Guid lockerId, CancellationToken cancellationToken)
    {
        if (role == nameof(UserRole.Administrator)) return;
        if (role != nameof(UserRole.LockerOperator) ||
            !await dbContext.OperatorAssignments.AnyAsync(item =>
                item.OperatorUserId == userId && item.LockerId == lockerId && item.RevokedAt == null,
                cancellationToken))
            throw new ForbiddenException(LockerErrorCodes.AccessDenied, "Locker is outside your operational scope.");
    }
}
