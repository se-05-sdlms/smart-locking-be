using smart_locking_be.Application.DTOs.DeliveryRequests;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IDeliveryRequestService
{
    // Issue #19: Guest Shipper Initiate & Submit Flow
    Task<InitiateDeliveryResponse> InitiateAsync(
        InitiateDeliveryRequest request,
        CancellationToken cancellationToken = default);

    Task<DeliveryRequestSummaryResponse> UploadImageAsync(
        Guid id,
        string guestSessionToken,
        UploadParcelImageRequest request,
        CancellationToken cancellationToken = default);

    Task<DeliveryRequestSummaryResponse> SubmitRecipientAsync(
        Guid id,
        string guestSessionToken,
        SubmitRecipientPhoneRequest request,
        CancellationToken cancellationToken = default);

    Task<int> ExpireStartedSessionsAsync(CancellationToken cancellationToken = default);

    // Issue #20: Resident Delivery Approval Flow
    Task<IReadOnlyCollection<PendingDeliveryRequestResponse>> GetPendingRequestsForResidentAsync(
        Guid residentUserId,
        CancellationToken cancellationToken = default);

    Task<DeliveryRequestSummaryResponse> ApproveDeliveryRequestAsync(
        Guid residentUserId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<DeliveryRequestSummaryResponse> RejectDeliveryRequestAsync(
        Guid residentUserId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<int> ExpirePendingApprovalsAsync(CancellationToken cancellationToken = default);

    // Issue #21: Compartment Reservation Flow
    Task<CompartmentReservationResponse> ReserveCompartmentAsync(
        Guid requestId,
        string guestSessionToken,
        CancellationToken cancellationToken = default);

    Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default);

    // Issue #22: Shipper Drop-off / Confirm Parcel Deposited Flow
    Task<DropOffConfirmationResponse> ConfirmDropOffAsync(
        Guid requestId,
        string guestSessionToken,
        CancellationToken cancellationToken = default);
}
