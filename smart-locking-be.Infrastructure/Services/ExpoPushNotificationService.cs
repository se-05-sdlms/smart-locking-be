using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ExpoPushNotificationService(
    ApplicationDbContext dbContext,
    HttpClient httpClient,
    TimeProvider timeProvider,
    ILogger<ExpoPushNotificationService> logger,
    IOperationsRealtimeNotifier? realtimeNotifier = null) : IPushNotificationService
{
    private const string ExpoPushEndpoint = "https://exp.host/--/api/v2/push/send";
    private const string DeliveryApprovalRequestType = "DeliveryApprovalRequested";
    private const string ParcelStoredType = "ParcelStored";

    public Guid EnqueueDeliveryApprovalRequest(
        Guid residentUserId,
        Guid deliveryRequestId,
        string lockerCode)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string title = "Có yêu cầu giao hàng mới";
        string message = $"Shipper đang yêu cầu gửi kiện hàng vào tủ {lockerCode}.";

        Notification inAppNotification = CreateNotification(
            residentUserId,
            deliveryRequestId,
            null,
            null,
            DeliveryApprovalRequestType,
            NotificationChannel.InApp,
            title,
            message,
            NotificationDeliveryStatus.Sent,
            now);
        inAppNotification.SentAt = now;

        Notification pushNotification = CreateNotification(
            residentUserId,
            deliveryRequestId,
            null,
            null,
            DeliveryApprovalRequestType,
            NotificationChannel.Push,
            title,
            message,
            NotificationDeliveryStatus.Pending,
            now);

        dbContext.Notifications.AddRange(inAppNotification, pushNotification);
        return pushNotification.Id;
    }

    public Guid EnqueueParcelStored(
        Guid residentUserId,
        Guid deliveryRequestId,
        Guid parcelId,
        string lockerCode,
        string compartmentCode)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string title = "Kiện hàng đã được lưu";
        string message = $"Kiện hàng của bạn đã được lưu tại tủ {lockerCode}, ngăn {compartmentCode}.";
        Notification inAppNotification = CreateNotification(
            residentUserId, deliveryRequestId, null, parcelId, ParcelStoredType,
            NotificationChannel.InApp, title, message, NotificationDeliveryStatus.Sent, now);
        inAppNotification.SentAt = now;
        Notification pushNotification = CreateNotification(
            residentUserId, deliveryRequestId, null, parcelId, ParcelStoredType,
            NotificationChannel.Push, title, message, NotificationDeliveryStatus.Pending, now);

        dbContext.Notifications.AddRange(inAppNotification, pushNotification);
        return pushNotification.Id;
    }

    public Guid EnqueueReturnNotification(
        Guid residentUserId,
        Guid returnRequestId,
        string type,
        string title,
        string message)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Notification inApp = CreateNotification(
            residentUserId, null, returnRequestId, null, type,
            NotificationChannel.InApp, title, message, NotificationDeliveryStatus.Sent, now);
        inApp.SentAt = now;
        Notification push = CreateNotification(
            residentUserId, null, returnRequestId, null, type,
            NotificationChannel.Push, title, message, NotificationDeliveryStatus.Pending, now);
        dbContext.Notifications.AddRange(inApp, push);
        return push.Id;
    }

    public Guid EnqueueParcelTransferred(
        Guid residentUserId,
        Guid deliveryRequestId,
        Guid parcelId,
        string collectionAddress)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        const string type = "ParcelTransferred";
        const string title = "Kiện hàng đã chuyển điểm tập kết";
        string message = $"Nhận kiện tại {collectionAddress}.";
        Notification inApp = CreateNotification(
            residentUserId, deliveryRequestId, null, parcelId, type,
            NotificationChannel.InApp, title, message, NotificationDeliveryStatus.Sent, now);
        inApp.SentAt = now;
        Notification push = CreateNotification(
            residentUserId, deliveryRequestId, null, parcelId, type,
            NotificationChannel.Push, title, message, NotificationDeliveryStatus.Pending, now);
        dbContext.Notifications.AddRange(inApp, push);
        return push.Id;
    }

    public Guid EnqueueParcelPickupReminder(
        Guid residentUserId,
        Guid deliveryRequestId,
        Guid parcelId,
        string lockerCode,
        int daysUntilTransfer,
        bool isOverdue)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        const string type = "ParcelPickupReminder";
        string title = isOverdue ? "Kiện hàng đã quá hạn" : "Nhắc nhận kiện hàng";
        string message = isOverdue
            ? $"Kiện tại tủ {lockerCode} đã quá hạn. Còn {daysUntilTransfer} ngày trước khi chuyển điểm tập kết."
            : $"Vui lòng nhận kiện tại tủ {lockerCode}. Còn {daysUntilTransfer} ngày trước khi chuyển điểm tập kết.";
        Notification inApp = CreateNotification(
            residentUserId, deliveryRequestId, null, parcelId, type,
            NotificationChannel.InApp, title, message, NotificationDeliveryStatus.Sent, now);
        inApp.SentAt = now;
        Notification push = CreateNotification(
            residentUserId, deliveryRequestId, null, parcelId, type,
            NotificationChannel.Push, title, message, NotificationDeliveryStatus.Pending, now);
        dbContext.Notifications.AddRange(inApp, push);
        return push.Id;
    }

    public async Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendCoreAsync(notificationId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to send Expo push notification {NotificationId}.",
                notificationId);

            try
            {
                await MarkFailedAsync(notificationId);
            }
            catch (Exception persistenceException)
            {
                logger.LogError(
                    persistenceException,
                    "Failed to persist the failed state for push notification {NotificationId}.",
                    notificationId);
            }
        }
    }

    public async Task<int> RetryPendingDeliveryApprovalNotificationsAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<Guid> notificationIds = await dbContext.Notifications
            .AsNoTracking()
            .Where(notification =>
                notification.Type == DeliveryApprovalRequestType &&
                notification.Channel == NotificationChannel.Push &&
                notification.DeliveryStatus != NotificationDeliveryStatus.Sent &&
                notification.DeliveryRequest != null &&
                notification.DeliveryRequest.Status == DeliveryRequestStatus.PendingApproval &&
                notification.DeliveryRequest.ApprovalExpiresAt > now)
            .OrderBy(notification => notification.CreatedAt)
            .Select(notification => notification.Id)
            .ToListAsync(cancellationToken);

        foreach (Guid notificationId in notificationIds)
        {
            await TrySendAsync(notificationId, cancellationToken);
        }

        return notificationIds.Count;
    }

    private async Task SendCoreAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        Notification notification = await dbContext.Notifications
            .SingleOrDefaultAsync(item => item.Id == notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy push notification '{notificationId}'.");

        if (realtimeNotifier is not null)
        {
            await realtimeNotifier.PublishToUserAsync(notification.UserId, new RealtimeEvent(
                notification.Type,
                notification.ParcelId ?? notification.ReturnRequestId ?? notification.DeliveryRequestId ?? notification.IncidentId,
                null,
                "Updated",
                notification.Message,
                notification.CreatedAt), cancellationToken);
        }

        if (notification.Channel != NotificationChannel.Push ||
            notification.DeliveryStatus == NotificationDeliveryStatus.Sent)
        {
            return;
        }

        List<DeviceInstallation> devices = await dbContext.DeviceInstallations
            .Where(device => device.UserId == notification.UserId && device.IsActive)
            .OrderBy(device => device.CreatedAt)
            .ToListAsync(cancellationToken);

        if (devices.Count == 0)
        {
            notification.DeliveryStatus = NotificationDeliveryStatus.Failed;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var messages = devices.Select(device => new
        {
            to = device.ExpoPushToken,
            title = notification.Title,
            body = notification.Message,
            sound = "default",
            priority = "high",
            data = new
            {
                type = notification.Type,
                deliveryRequestId = notification.DeliveryRequestId,
                returnRequestId = notification.ReturnRequestId,
                parcelId = notification.ParcelId,
                incidentId = notification.IncidentId
            }
        });

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            ExpoPushEndpoint,
            messages,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using JsonDocument payload = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
            ?? throw new JsonException("Expo Push API trả về nội dung rỗng.");

        JsonElement[] tickets = payload.RootElement.GetProperty("data").EnumerateArray().ToArray();
        bool sent = false;

        for (int index = 0; index < Math.Min(tickets.Length, devices.Count); index++)
        {
            JsonElement ticket = tickets[index];
            string? status = ticket.TryGetProperty("status", out JsonElement statusElement)
                ? statusElement.GetString()
                : null;
            sent |= status == "ok";

            if (IsDeviceNotRegistered(ticket))
            {
                devices[index].IsActive = false;
                devices[index].UpdatedAt = timeProvider.GetUtcNow();
            }
        }

        notification.DeliveryStatus = sent
            ? NotificationDeliveryStatus.Sent
            : NotificationDeliveryStatus.Failed;
        notification.SentAt = sent ? timeProvider.GetUtcNow() : null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(Guid notificationId)
    {
        Notification? notification = await dbContext.Notifications.FindAsync(
            [notificationId],
            CancellationToken.None);

        if (notification is null || notification.DeliveryStatus == NotificationDeliveryStatus.Sent)
        {
            return;
        }

        notification.DeliveryStatus = NotificationDeliveryStatus.Failed;
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static Notification CreateNotification(
        Guid residentUserId,
        Guid? deliveryRequestId,
        Guid? returnRequestId,
        Guid? parcelId,
        string type,
        NotificationChannel channel,
        string title,
        string message,
        NotificationDeliveryStatus deliveryStatus,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = residentUserId,
            DeliveryRequestId = deliveryRequestId,
            ReturnRequestId = returnRequestId,
            ParcelId = parcelId,
            Type = type,
            Channel = channel,
            Title = title,
            Message = message,
            DeliveryStatus = deliveryStatus,
            CreatedAt = createdAt
        };

    private static bool IsDeviceNotRegistered(JsonElement ticket) =>
        ticket.TryGetProperty("details", out JsonElement details) &&
        details.TryGetProperty("error", out JsonElement error) &&
        error.GetString() == "DeviceNotRegistered";
}
