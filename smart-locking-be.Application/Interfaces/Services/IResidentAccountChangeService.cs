using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Residents;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IResidentAccountChangeService
{
    Task<PagedResult<ResidentAccountChangeResponse>> GetAsync(Guid actorId, int pageNumber, int pageSize, CancellationToken cancellationToken);
    Task<ResidentAccountChangeResponse> GetAsync(Guid actorId, Guid id, CancellationToken cancellationToken);
    Task<AccountChangeEligibilityResponse> EligibilityAsync(Guid actorId, Guid targetLockerId, CancellationToken cancellationToken);
    Task<ResidentAccountChangeResponse> CreateLockerAsync(Guid actorId, CreateLockerChangeRequest request, CancellationToken cancellationToken);
    Task RequestPhoneOtpAsync(Guid actorId, RequestPhoneChangeOtpRequest request, CancellationToken cancellationToken);
    Task<ResidentAccountChangeResponse> CreatePhoneAsync(Guid actorId, CreatePhoneChangeRequest request, CancellationToken cancellationToken);
    Task<ResidentAccountChangeResponse> ReviewAsync(Guid actorId, Guid id, ReviewAccountChangeRequest request, CancellationToken cancellationToken);
    Task<ResidentAccountChangeResponse> CancelAsync(Guid actorId, Guid id, CancellationToken cancellationToken);
}
