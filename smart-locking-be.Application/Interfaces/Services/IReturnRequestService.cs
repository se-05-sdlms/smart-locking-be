using smart_locking_be.Domain.Enums;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Returns;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IReturnRequestService
{
    Task<ReturnRequestResponse> CreateAsync(Guid residentUserId, CreateReturnRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<ReturnRequestResponse>> GetMineAsync(
        Guid residentUserId,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20);
    Task<ReturnRequestResponse> GetAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default);
    Task<ReturnUnlockResponse> OpenCompartmentAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default, LockerAccessMethod accessMethod = LockerAccessMethod.RemoteApp);
    Task FinalizeDepositAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default);
    Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default);
}
