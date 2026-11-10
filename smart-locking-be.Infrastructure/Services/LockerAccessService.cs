using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class LockerAccessService(
    ApplicationDbContext dbContext,
    ILockerCommandDispatcher commandDispatcher,
    TimeProvider timeProvider,
    IConfiguration configuration) : ILockerAccessService
{
    public async Task<OpenLockerResponse> OpenAsync(
        OpenLockerRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(request);
        await using var parcelTransaction = request.AccessType == LockerAccessType.ResidentPickup
            ? await ParcelTransitionLock.AcquireAsync(dbContext, request.ParcelId!.Value, cancellationToken)
            : null;
        if (request.AccessType == LockerAccessType.ResidentPickup)
        {
            bool available = await dbContext.Parcels.AnyAsync(item => item.Id == request.ParcelId &&
                item.DeliveryRequest.LockerId == request.LockerId &&
                item.DeliveryRequest.AllocatedCompartmentId == request.LockerCompartmentId &&
                (item.Status == ParcelStatus.Stored || item.Status == ParcelStatus.Overdue) &&
                item.MaxStorageUntil > timeProvider.GetUtcNow(), cancellationToken);
            if (!available)
                throw new ConflictException(ParcelErrorCodes.InvalidState, "Parcel is no longer available for pickup.");
        }
        await using var returnTransition = request.ReturnRequestId.HasValue
            ? await ReturnTransitionLock.AcquireAsync(dbContext, request.ReturnRequestId.Value, cancellationToken) : null;
        if (request.AccessType is LockerAccessType.ResidentReturnDropOff or LockerAccessType.ShipperReturnPickup)
        {
            ReturnRequest? returnRequest = await dbContext.ReturnRequests.Include(item => item.Cancellation)
                .Include(item => item.ResidentProfile).SingleOrDefaultAsync(item => item.Id == request.ReturnRequestId, cancellationToken);
            bool dropOff = request.AccessType == LockerAccessType.ResidentReturnDropOff;
            if (returnRequest is null || returnRequest.Cancellation is not null || returnRequest.LockerId != request.LockerId
                || returnRequest.AllocatedCompartmentId != request.LockerCompartmentId
                || (dropOff ? returnRequest.Status != ReturnRequestStatus.Allocated || returnRequest.ResidentProfile.UserId != request.UserId
                    : returnRequest.Status != ReturnRequestStatus.Deposited || string.IsNullOrWhiteSpace(returnRequest.ShipperSessionTokenHash)))
                throw new ConflictException(ReturnErrorCodes.InvalidState, "Return access is revoked or no longer available.");
        }


        LockerCompartment compartment = await dbContext.LockerCompartments
            .Include(item => item.Locker)
            .SingleOrDefaultAsync(item => item.Id == request.LockerCompartmentId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.InvalidCompartment, "Locker compartment not found.");

        if (compartment.LockerId != request.LockerId)
        {
            throw new BadRequestException(LockerErrorCodes.InvalidCompartment, "Compartment does not belong to the requested locker.");
        }

        bool monitoringEnabled = configuration.GetValue<bool>("LockerMonitoring:Enabled");
        if (monitoringEnabled && compartment.Locker.ConnectionStatus != LockerConnectionStatus.Online)
        {
            throw new ConflictException(LockerErrorCodes.Offline, "Locker is offline or has not reported its connection status.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        LockerAccessEvent accessEvent = new()
        {
            Id = Guid.NewGuid(),
            LockerId = request.LockerId,
            LockerCompartmentId = request.LockerCompartmentId,
            UserId = request.UserId,
            DeliveryRequestId = request.DeliveryRequestId,
            ParcelId = request.ParcelId,
            ReturnRequestId = request.ReturnRequestId,
            AccessType = request.AccessType,
            AccessMethod = request.AccessMethod,
            IpAddress = TrimToNull(request.IpAddress),
            DeviceContext = TrimToNull(request.DeviceContext),
            OccurredAt = now,
        };

        string? blockedReason = GetBlockedReason(compartment, monitoringEnabled);
        if (blockedReason is null && request.AccessType != LockerAccessType.OperatorEmergency &&
            !await dbContext.OperationalCompartments().AnyAsync(item => item.Id == compartment.Id, cancellationToken))
            blockedReason = "Locker or compartment is blocked by maintenance or an unresolved equipment incident.";
        if (blockedReason is not null)
        {
            accessEvent.Result = LockerAccessResult.Blocked;
            accessEvent.FailureReason = blockedReason;
        }
        else
        {
            try
            {
                await commandDispatcher.DispatchUnlockAsync(
                    new LockerUnlockCommand(
                        accessEvent.Id,
                        compartment.Locker.DeviceIdentifier,
                        compartment.HardwareChannel),
                    cancellationToken);
                accessEvent.Result = LockerAccessResult.Succeeded;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                accessEvent.Result = LockerAccessResult.Failed;
                accessEvent.FailureReason = "Could not send the unlock command to the locker.";
            }
        }

        dbContext.LockerAccessEvents.Add(accessEvent);
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = request.UserId,
            Action = "Locker.UnlockCommand",
            EntityType = nameof(LockerAccessEvent),
            EntityId = accessEvent.Id,
            Result = accessEvent.Result == LockerAccessResult.Succeeded ? AuditLogResult.Succeeded : AuditLogResult.Failed,
            Details = $"LockerId={request.LockerId}; CompartmentId={request.LockerCompartmentId}; AccessType={request.AccessType}; Result={accessEvent.Result}",
            OccurredAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (parcelTransaction is not null) await parcelTransaction.CommitAsync(cancellationToken);
        if (returnTransition is not null) await returnTransition.CommitAsync(cancellationToken);
        return Map(accessEvent, compartment);
    }

    private static string? GetBlockedReason(LockerCompartment compartment, bool monitoringEnabled)
    {
        if (compartment.Locker.OperationalStatus != LockerOperationalStatus.Operational)
        {
            return "Locker is not operational.";
        }
        if (monitoringEnabled && compartment.Locker.ConnectionStatus != LockerConnectionStatus.Online)
        {
            return "Locker is offline.";
        }
        if (compartment.OperationalStatus != LockerCompartmentOperationalStatus.Operational)
        {
            return "Locker compartment is not operational.";
        }
        return null;
    }

    private static void ValidateContext(OpenLockerRequest request)
    {
        bool missingContext = request.AccessType switch
        {
            LockerAccessType.ShipperDropOff => !request.DeliveryRequestId.HasValue,
            LockerAccessType.ResidentPickup => !request.ParcelId.HasValue,
            LockerAccessType.ResidentReturnDropOff or LockerAccessType.ShipperReturnPickup =>
                !request.ReturnRequestId.HasValue,
            _ => false,
        };
        if (missingContext)
        {
            throw new BadRequestException(CommonErrorCodes.InvalidRequest, "A business identifier is required to open the locker.");
        }
        if (request.AccessMethod == LockerAccessMethod.RemoteApp && !request.UserId.HasValue)
        {
            throw new BadRequestException(AuthErrorCodes.AuthenticationRequired, "Opening a locker from the app requires an authenticated user.");
        }
        if (TrimToNull(request.IpAddress)?.Length > 45)
        {
            throw BadRequestException.For("ipAddress", "IP address is invalid.");
        }
    }

    private static OpenLockerResponse Map(LockerAccessEvent accessEvent, LockerCompartment compartment) => new(
        accessEvent.Id,
        accessEvent.LockerId,
        accessEvent.LockerCompartmentId,
        compartment.Locker.DeviceIdentifier,
        compartment.HardwareChannel,
        accessEvent.Result,
        accessEvent.FailureReason,
        accessEvent.OccurredAt);

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
