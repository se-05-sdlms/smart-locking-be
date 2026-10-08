using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ILockerService
{
    Task<PagedResult<LockerSummaryResponse>> GetLockersAsync(
        Guid userId,
        string userRole,
        string? search = null,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20);

    Task<LockerDetailResponse> GetLockerByIdAsync(Guid userId, string userRole, Guid lockerId, CancellationToken cancellationToken = default);

    Task<LockerDetailResponse> CreateLockerAsync(CreateLockerRequest request, CancellationToken cancellationToken = default);

    Task<LockerDetailResponse> UpdateLockerAsync(Guid lockerId, UpdateLockerRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<LockerCompartmentResponse>> GetCompartmentsAsync(Guid userId, string userRole, Guid lockerId, CancellationToken cancellationToken = default);

    Task<LockerCompartmentResponse> CreateCompartmentAsync(Guid lockerId, CreateCompartmentRequest request, CancellationToken cancellationToken = default);

    Task<LockerDetailResponse> UpdateOperationalStatusAsync(Guid userId, string userRole, Guid lockerId, UpdateOperationalStatusRequest request, CancellationToken cancellationToken = default);

    Task<LockerCompartmentResponse> UpdateCompartmentOperationalStatusAsync(Guid userId, string userRole, Guid lockerId, Guid compartmentId, UpdateOperationalStatusRequest request, CancellationToken cancellationToken = default);
}
