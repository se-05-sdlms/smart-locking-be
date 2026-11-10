using smart_locking_be.Application.DTOs.Returns;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IReturnPickupSessionService
{
    Task<ReturnPickupSessionResponse> CreateAsync(
        ValidateReturnPickupRequest request,
        CancellationToken cancellationToken = default);

    Task<ReturnPickupSessionResponse> GetAsync(
        Guid id,
        string guestSessionToken,
        CancellationToken cancellationToken = default);

    Task<ReturnPickupSessionResponse> OpenCompartmentAsync(
        Guid id,
        string guestSessionToken,
        CancellationToken cancellationToken = default);

    Task FinalizePickupAsync(
        Guid id,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}
