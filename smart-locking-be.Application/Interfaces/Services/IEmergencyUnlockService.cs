using smart_locking_be.Application.DTOs.Operations;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IEmergencyUnlockService
{
    Task<EmergencyUnlockResponse> CreateAsync(
        Guid userId,
        string role,
        EmergencyUnlockRequest request,
        CancellationToken cancellationToken = default);
}
