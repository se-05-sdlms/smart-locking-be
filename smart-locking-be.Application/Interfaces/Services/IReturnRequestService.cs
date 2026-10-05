using smart_locking_be.Application.DTOs.Returns;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IReturnRequestService
{
    Task<ReturnRequestResponse> CreateAsync(Guid residentUserId, CreateReturnRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ReturnRequestResponse>> GetMineAsync(Guid residentUserId, CancellationToken cancellationToken = default);
    Task<ReturnRequestResponse> GetAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default);
    Task<ReturnUnlockResponse> AllocateAndOpenAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default);
    Task<ReturnDepositResponse> ConfirmDepositAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default);
    Task<ReturnPickupSessionResponse> ValidatePickupAsync(ValidateReturnPickupRequest request, CancellationToken cancellationToken = default);
    Task<ReturnPickupSessionResponse> OpenForPickupAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default);
    Task<ReturnPickupCompleteResponse> ConfirmPickupAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default);
    Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default);
}
