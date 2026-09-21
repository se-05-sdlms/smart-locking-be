using smart_locking_be.Application.DTOs.DeliveryRequests;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IDeliveryRequestService
{
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
}
