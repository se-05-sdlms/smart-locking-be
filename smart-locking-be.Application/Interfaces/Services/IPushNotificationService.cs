namespace smart_locking_be.Application.Interfaces.Services;

public interface IPushNotificationService
{
    Guid EnqueueDeliveryApprovalRequest(
        Guid residentUserId,
        Guid deliveryRequestId,
        string lockerCode);

    Task TrySendAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default);

    Task<int> RetryPendingDeliveryApprovalNotificationsAsync(
        CancellationToken cancellationToken = default);
}
