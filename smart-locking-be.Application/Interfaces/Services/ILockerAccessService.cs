using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ILockerAccessService
{
    Task<OpenLockerResponse> OpenAsync(
        OpenLockerRequest request,
        CancellationToken cancellationToken = default);
}
