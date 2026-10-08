using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Parcels;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IParcelService
{
    Task<PagedResult<ParcelListItemResponse>> GetParcelsAsync(
        Guid userId,
        string role,
        ParcelListView view,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken,
        int pageNumber = 1,
        int pageSize = 20);

    Task<ParcelDetailResponse> GetParcelAsync(
        Guid userId,
        string role,
        Guid parcelId,
        CancellationToken cancellationToken);

    Task<PagedResult<ParcelStatusHistoryResponse>> GetHistoryAsync(
        Guid userId,
        string role,
        Guid parcelId,
        CancellationToken cancellationToken,
        int pageNumber = 1,
        int pageSize = 20);

    Task<PickupUnlockResponse> OpenCompartmentAsync(
        Guid residentUserId,
        Guid parcelId,
        string? ipAddress,
        string? deviceContext,
        CancellationToken cancellationToken = default);

    Task FinalizeRetrievalAsync(
        Guid parcelId,
        Guid? residentUserId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task<OverdueTransferResponse> TransferOverdueAsync(
        Guid userId,
        string role,
        Guid parcelId,
        CancellationToken cancellationToken = default);
}
