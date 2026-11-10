using smart_locking_be.Application.DTOs.Users;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IOperatorSupportService
{
    Task<LockerSupportResponse> GetPublicAsync(string code, CancellationToken ct);
    Task<LockerSupportResponse> GetForUserAsync(Guid userId, string role, Guid lockerId, CancellationToken ct);
    Task<OperatorSupportContactResponse?> GetContactAsync(Guid operatorId, CancellationToken ct);
    Task<OperatorSupportContactResponse> SaveContactAsync(Guid adminId, Guid operatorId, SaveOperatorSupportContactRequest request, CancellationToken ct);
    Task<SaveLockerSupportSettingsRequest?> GetSettingsAsync(Guid lockerId, CancellationToken ct);
    Task<SaveLockerSupportSettingsRequest> SaveSettingsAsync(Guid adminId, Guid lockerId, SaveLockerSupportSettingsRequest request, CancellationToken ct);
}
