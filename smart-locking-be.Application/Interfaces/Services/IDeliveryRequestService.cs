using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.DeliveryRequests;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IDeliveryRequestService
{
    Task<InitiateDeliveryResponse> CreateAsync(
        InitiateDeliveryRequest request,
        CancellationToken cancellationToken = default);

    Task<DeliveryRequestSummaryResponse> SubmitAsync(
        Guid id,
        string guestSessionToken,
        SubmitDeliveryRequest request,
        CancellationToken cancellationToken = default);

    Task<WaybillOcrResponse> ScanWaybillAsync(
        Guid id,
        string guestSessionToken,
        Stream image,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<ResolveRecipientResponse> ResolveRecipientAsync(
        Guid id,
        string guestSessionToken,
        ResolveRecipientRequest request,
        CancellationToken cancellationToken = default);

    Task<GuestDeliveryStatusResponse> GetAsync(
        Guid id,
        string guestSessionToken,
        CancellationToken cancellationToken = default);

    Task<int> ExpireStartedSessionsAsync(CancellationToken cancellationToken = default);

    Task<DeliveryRequestSummaryResponse> CancelAsync(
        Guid id,
        string guestSessionToken,
        CancellationToken cancellationToken = default);

    Task<ResidentDeliveryRequestResponse> GetForResidentAsync(
        Guid residentUserId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<PendingDeliveryRequestResponse>> GetPendingRequestsForResidentAsync(
        Guid residentUserId,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20);

    Task<DeliveryRequestSummaryResponse> ApproveDeliveryRequestAsync(
        Guid residentUserId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<DeliveryRequestSummaryResponse> RejectDeliveryRequestAsync(
        Guid residentUserId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<int> ExpirePendingApprovalsAsync(CancellationToken cancellationToken = default);

    Task<CompartmentReservationResponse> OpenCompartmentAsync(
        Guid requestId,
        string guestSessionToken,
        CancellationToken cancellationToken = default);

    Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default);

    Task FinalizeDropOffAsync(
        Guid requestId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}
