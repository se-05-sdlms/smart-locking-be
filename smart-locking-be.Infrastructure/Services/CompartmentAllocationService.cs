using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class CompartmentAllocationService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : ICompartmentAllocationService
{
    public async Task<CompartmentReservation?> ReserveAvailableAsync(
        Guid lockerId,
        Guid? deliveryRequestId,
        Guid? returnRequestId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ValidateOperation(deliveryRequestId, returnRequestId);
        DateTimeOffset now = timeProvider.GetUtcNow();
        await using IDbContextTransaction? transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        LockerCompartment? compartment = await dbContext.LockerCompartments
            .Where(item => item.LockerId == lockerId &&
                           item.OperationalStatus == LockerCompartmentOperationalStatus.Operational &&
                           item.DoorStatus == DoorStatus.Closed)
            .Where(item => !dbContext.CompartmentReservations.Any(reservation =>
                reservation.LockerCompartmentId == item.Id &&
                reservation.ReleasedAt == null &&
                reservation.ExpiresAt > now))
            .Where(item => !dbContext.Parcels.Any(parcel =>
                parcel.DeliveryRequest.AllocatedCompartmentId == item.Id &&
                (parcel.Status == ParcelStatus.Stored || parcel.Status == ParcelStatus.Overdue)))
            .Where(item => !dbContext.ReturnRequests.Any(request =>
                request.AllocatedCompartmentId == item.Id &&
                (request.Status == ReturnRequestStatus.Allocated || request.Status == ReturnRequestStatus.Deposited)))
            .OrderBy(item => item.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (compartment is null)
        {
            return null;
        }

        CompartmentReservation reservation = new()
        {
            Id = Guid.NewGuid(),
            LockerCompartmentId = compartment.Id,
            LockerCompartment = compartment,
            DeliveryRequestId = deliveryRequestId,
            ReturnRequestId = returnRequestId,
            ReservedAt = now,
            ExpiresAt = expiresAt,
            CreatedAt = now
        };
        dbContext.CompartmentReservations.Add(reservation);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return reservation;
    }

    public async Task ReleaseAsync(
        Guid? deliveryRequestId,
        Guid? returnRequestId,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateOperation(deliveryRequestId, returnRequestId);
        List<CompartmentReservation> reservations = await dbContext.CompartmentReservations
            .Where(item => item.ReleasedAt == null &&
                           (deliveryRequestId.HasValue
                               ? item.DeliveryRequestId == deliveryRequestId
                               : item.ReturnRequestId == returnRequestId))
            .ToListAsync(cancellationToken);
        foreach (CompartmentReservation reservation in reservations)
        {
            reservation.ReleasedAt = releasedAt;
        }
    }

    private static void ValidateOperation(Guid? deliveryRequestId, Guid? returnRequestId)
    {
        if (deliveryRequestId.HasValue == returnRequestId.HasValue)
        {
            throw new ArgumentException("Exactly one delivery or return request must own the reservation.");
        }
    }
}
