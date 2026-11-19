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
        await using var unlockTransaction = await EmergencyUnlockLock.AcquireAsync(dbContext, compartment.Id, cancellationToken);
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

        if (entity.Result == EmergencyUnlockResult.Succeeded)
        {
            if (!string.IsNullOrWhiteSpace(request.PackageAction) &&
                !string.Equals(request.PackageAction.Trim(), "Keep", StringComparison.OrdinalIgnoreCase))
            {
                await HandlePackageActionAsync(userId, request.PackageAction.Trim(), compartment.Id, request.Reason, now, cancellationToken);
            }

            if (request.SetOutOfService)
            {
                compartment.OperationalStatus = LockerCompartmentOperationalStatus.OutOfService;
                compartment.UpdatedAt = now;
                dbContext.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = userId,
                    Action = "Compartment.StatusUpdated",
                    EntityType = nameof(LockerCompartment),
                    EntityId = compartment.Id,
                    Result = AuditLogResult.Succeeded,
                    Details = "OperationalStatus=OutOfService; Reason=EmergencyUnlock",
                    OccurredAt = now
                });
            }
        }

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
            Details = $"LockerId={request.LockerId}; Result={entity.Result}; Reason={entity.Reason}; PackageAction={request.PackageAction ?? "Keep"}; SetOutOfService={request.SetOutOfService}",
            OccurredAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (unlockTransaction is not null) await unlockTransaction.CommitAsync(cancellationToken);
        return new EmergencyUnlockResponse(entity.Id, entity.Result, compartment.Code, entity.RequestedAt);
    }

    private async Task HandlePackageActionAsync(
        Guid userId,
        string packageAction,
        Guid compartmentId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var parcel = await dbContext.Parcels
            .Include(p => p.DeliveryRequest)
            .SingleOrDefaultAsync(p => p.DeliveryRequest.AllocatedCompartmentId == compartmentId &&
                (p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue), cancellationToken);

        if (parcel is not null)
        {
            var previousStatus = parcel.Status;
            if (string.Equals(packageAction, "Handover", StringComparison.OrdinalIgnoreCase))
            {
                parcel.Status = ParcelStatus.Retrieved;
                parcel.RetrievedAt = now;
                parcel.UpdatedAt = now;
                parcel.DeliveryRequest.CompartmentReleasedAt = now;
                parcel.DeliveryRequest.UpdatedAt = now;

                var reservations = await dbContext.CompartmentReservations
                    .Where(r => r.DeliveryRequestId == parcel.DeliveryRequestId && r.ReleasedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var reservation in reservations)
                {
                    reservation.ReleasedAt = now;
                }

                dbContext.ParcelStatusHistories.Add(new ParcelStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ParcelId = parcel.Id,
                    FromStatus = previousStatus,
                    ToStatus = ParcelStatus.Retrieved,
                    Reason = $"Bàn giao trực tiếp cho cư dân: {reason}",
                    ChangedByUserId = userId,
                    ChangedAt = now
                });

                dbContext.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = userId,
                    Action = "Parcel.HandoverDirect",
                    EntityType = nameof(Parcel),
                    EntityId = parcel.Id,
                    Result = AuditLogResult.Succeeded,
                    Details = $"Reason={reason}",
                    OccurredAt = now
                });
            }
            else if (string.Equals(packageAction, "Remove", StringComparison.OrdinalIgnoreCase))
            {
                parcel.Status = ParcelStatus.Removed;
                parcel.RemovedAt = now;
                parcel.RemovedByUserId = userId;
                parcel.RemovalReason = reason;
                parcel.UpdatedAt = now;
                parcel.DeliveryRequest.CompartmentReleasedAt = now;
                parcel.DeliveryRequest.UpdatedAt = now;

                var reservations = await dbContext.CompartmentReservations
                    .Where(r => r.DeliveryRequestId == parcel.DeliveryRequestId && r.ReleasedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var reservation in reservations)
                {
                    reservation.ReleasedAt = now;
                }

                dbContext.ParcelStatusHistories.Add(new ParcelStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ParcelId = parcel.Id,
                    FromStatus = previousStatus,
                    ToStatus = ParcelStatus.Removed,
                    Reason = reason,
                    ChangedByUserId = userId,
                    ChangedAt = now
                });

                dbContext.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = userId,
                    Action = "Parcel.EmergencyRemoved",
                    EntityType = nameof(Parcel),
                    EntityId = parcel.Id,
                    Result = AuditLogResult.Succeeded,
                    Details = $"Reason={reason}",
                    OccurredAt = now
                });
            }
        }
        else
        {
            var returnRequest = await dbContext.ReturnRequests
                .SingleOrDefaultAsync(r => r.AllocatedCompartmentId == compartmentId &&
                    (r.Status == ReturnRequestStatus.Allocated || r.Status == ReturnRequestStatus.Deposited), cancellationToken);

            if (returnRequest is not null)
            {
                if (string.Equals(packageAction, "Handover", StringComparison.OrdinalIgnoreCase))
                {
                    returnRequest.Status = ReturnRequestStatus.Completed;
                    returnRequest.UpdatedAt = now;
                }
                else if (string.Equals(packageAction, "Remove", StringComparison.OrdinalIgnoreCase))
                {
                    returnRequest.Status = ReturnRequestStatus.Cancelled;
                    returnRequest.UpdatedAt = now;
                }
            }
        }
    }

    private async Task EnsureAccessAsync(Guid userId, string role, Guid lockerId, CancellationToken cancellationToken)
    {
        if (role == nameof(UserRole.Administrator)) return;
        if (role != nameof(UserRole.LockerOperator) ||
            !await dbContext.EffectiveOperatorAssignments(timeProvider.GetUtcNow()).AnyAsync(item =>
                item.OperatorUserId == userId && item.LockerId == lockerId && item.RevokedAt == null,
                cancellationToken))
            throw new ForbiddenException(LockerErrorCodes.AccessDenied, "Locker is outside your operational scope.");
    }
}
