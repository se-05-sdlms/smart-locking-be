using smart_locking_be.Application.DTOs.Returns;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IReturnRequestService
{
    Task<ReturnRequestResponse> CreateAsync(Guid residentUserId, CreateReturnRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ReturnRequestResponse>> GetMineAsync(Guid residentUserId, CancellationToken cancellationToken = default);
    Task<ReturnRequestResponse> GetAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default);
    Task<ReturnUnlockResponse> OpenCompartmentAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default);
    Task FinalizeDepositAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default);
    Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default);
}
