using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ILockerCommissioningService
{
    Task<LockerCommissioningResponse> GetAsync(Guid lockerId, CancellationToken cancellationToken = default);
    Task<LockerCommissioningResponse> BindAsync(Guid actorId, Guid lockerId, BindCommissioningDeviceRequest request, CancellationToken cancellationToken = default);
    Task<LockerCommissioningResponse> ConfigureAsync(Guid actorId, Guid lockerId, ConfigureCommissioningRequest request, CancellationToken cancellationToken = default);
    Task<LockerCommissioningResponse> VerifyAsync(Guid actorId, Guid lockerId, VerifyCommissioningRequest request, CancellationToken cancellationToken = default);
    Task<LockerCommissioningResponse> HandoverAsync(Guid actorId, Guid lockerId, HandoverCommissioningRequest request, CancellationToken cancellationToken = default);
}
