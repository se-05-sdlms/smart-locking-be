using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ExpoPushNotificationService(
    ApplicationDbContext dbContext,
    HttpClient httpClient,
    TimeProvider timeProvider,
    ILogger<ExpoPushNotificationService> logger) : IPushNotificationService
{
    private const string ExpoPushEndpoint = "https://exp.host/--/api/v2/push/send";
    private const string DeliveryApprovalRequestType = "DeliveryApprovalRequested";

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
            NotificationChannel.InApp,
            title,
            message,
            NotificationDeliveryStatus.Sent,
            now);
        inAppNotification.SentAt = now;

        Notification pushNotification = CreateNotification(
            residentUserId,
            deliveryRequestId,
            NotificationChannel.Push,
            title,
            message,
            NotificationDeliveryStatus.Pending,
            now);

        dbContext.Notifications.AddRange(inAppNotification, pushNotification);
        return pushNotification.Id;
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

        if (notification.Channel != NotificationChannel.Push ||
            notification.DeliveryStatus == NotificationDeliveryStatus.Sent)
        {
            return;
        }

        if (!notification.DeliveryRequestId.HasValue)
        {
            throw new InvalidOperationException("Push notification không liên kết với yêu cầu giao hàng.");
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
            data = new
            {
                type = "delivery_request",
                deliveryRequestId = notification.DeliveryRequestId.Value
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
        Guid deliveryRequestId,
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
            Type = DeliveryApprovalRequestType,
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
