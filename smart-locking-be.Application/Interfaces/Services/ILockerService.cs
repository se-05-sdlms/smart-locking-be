using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ILockerService
{
    Task<PublicLockerResponse> GetPublicLockerAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<LockerSummaryResponse>> GetLockersAsync(Guid userId, string userRole, string? search = null, CancellationToken cancellationToken = default);

    Task<LockerDetailResponse> GetLockerByIdAsync(Guid userId, string userRole, Guid lockerId, CancellationToken cancellationToken = default);

    Task<LockerDetailResponse> CreateLockerAsync(Guid actorUserId, CreateLockerRequest request, CancellationToken cancellationToken = default);

    Task<LockerDetailResponse> UpdateLockerAsync(Guid actorUserId, Guid lockerId, UpdateLockerRequest request, CancellationToken cancellationToken = default);

    Task<bool> SoftDeleteLockerAsync(Guid actorUserId, Guid lockerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<LockerCompartmentResponse>> GetCompartmentsAsync(Guid userId, string userRole, Guid lockerId, CancellationToken cancellationToken = default);

    Task<LockerCompartmentResponse> CreateCompartmentAsync(Guid actorUserId, Guid lockerId, CreateCompartmentRequest request, CancellationToken cancellationToken = default);

    Task<LockerCompartmentResponse> UpdateCompartmentAsync(Guid actorUserId, Guid lockerId, Guid compartmentId,
        UpdateCompartmentRequest request, CancellationToken cancellationToken = default);

    Task<LockerCompartmentResponse> UpdateCompartmentStatusAsync(Guid userId, string userRole, Guid lockerId, Guid compartmentId, UpdateCompartmentStatusRequest request, CancellationToken cancellationToken = default);
}
