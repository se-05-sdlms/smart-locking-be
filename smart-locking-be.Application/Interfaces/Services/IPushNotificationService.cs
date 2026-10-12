namespace smart_locking_be.Application.Interfaces.Services;

public interface IPushNotificationService
{
    Guid EnqueueDeliveryApprovalRequest(
        Guid residentUserId,
        Guid deliveryRequestId,
        string lockerCode);

    Guid EnqueueReturnNotification(
        Guid residentUserId,
        Guid returnRequestId,
        string type,
        string title,
        string message);

    Task TrySendAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default);

    Task<int> RetryPendingDeliveryApprovalNotificationsAsync(
        CancellationToken cancellationToken = default);
    Guid EnqueueParcelTransferred(
        Guid residentUserId,
        Guid deliveryRequestId,
        Guid parcelId,
        string collectionAddress);

}
