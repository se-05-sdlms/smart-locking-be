using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ILockerService
{
    Task<IReadOnlyCollection<LockerSummaryResponse>> GetLockersAsync(Guid userId, string userRole, string? search = null, CancellationToken cancellationToken = default);

    Task<LockerDetailResponse> GetLockerByIdAsync(Guid userId, string userRole, Guid lockerId, CancellationToken cancellationToken = default);

    Task<LockerDetailResponse> CreateLockerAsync(CreateLockerRequest request, CancellationToken cancellationToken = default);

    Task<LockerDetailResponse> UpdateLockerAsync(Guid lockerId, UpdateLockerRequest request, CancellationToken cancellationToken = default);

    Task<bool> SoftDeleteLockerAsync(Guid lockerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<LockerCompartmentResponse>> GetCompartmentsAsync(Guid userId, string userRole, Guid lockerId, CancellationToken cancellationToken = default);

    Task<LockerCompartmentResponse> CreateCompartmentAsync(Guid lockerId, CreateCompartmentRequest request, CancellationToken cancellationToken = default);

    Task<LockerCompartmentResponse> UpdateCompartmentStatusAsync(Guid userId, string userRole, Guid lockerId, Guid compartmentId, UpdateCompartmentStatusRequest request, CancellationToken cancellationToken = default);
}
