using smart_locking_be.Application.DTOs.Parcels;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IParcelService
{
    Task<IReadOnlyCollection<ParcelListItemResponse>> GetParcelsAsync(
        Guid userId,
        string role,
        ParcelListView view,
        string? search,
        CancellationToken cancellationToken);

    Task<ParcelDetailResponse> GetParcelAsync(
        Guid userId,
        string role,
        Guid parcelId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ParcelStatusHistoryResponse>> GetHistoryAsync(
        Guid userId,
        string role,
        Guid parcelId,
        CancellationToken cancellationToken);
}
