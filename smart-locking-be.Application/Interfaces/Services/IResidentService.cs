using smart_locking_be.Application.DTOs.Residents;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IResidentService
{
    Task<ResidentProfileResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<ResidentProfileResponse> UpdateProfileAsync(Guid userId, UpdateResidentProfileRequest request, CancellationToken cancellationToken = default);

    Task<ResidentProfileResponse> UpdateApprovalModeAsync(Guid userId, UpdateApprovalModeRequest request, CancellationToken cancellationToken = default);

    Task<PersonalQrResponse> GetPersonalQrAsync(Guid userId, CancellationToken cancellationToken = default);
}
