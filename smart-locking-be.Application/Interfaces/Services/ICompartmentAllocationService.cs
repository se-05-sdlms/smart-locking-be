using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ICompartmentAllocationService
{
    Task<CompartmentReservation?> ReserveAvailableAsync(
        Guid lockerId,
        Guid? deliveryRequestId,
        Guid? returnRequestId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        Guid? deliveryRequestId,
        Guid? returnRequestId,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken = default);
}
