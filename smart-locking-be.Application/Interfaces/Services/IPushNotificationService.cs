namespace smart_locking_be.Application.Interfaces.Services;

public interface IPushNotificationService
{
    Task SendDeliveryApprovalRequestAsync(
        Guid residentUserId,
        Guid deliveryRequestId,
        string lockerCode,
        CancellationToken cancellationToken = default);
}
