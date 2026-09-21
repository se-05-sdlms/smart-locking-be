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
    ILogger<ExpoPushNotificationService> logger) : IPushNotificationService
{
    private const string ExpoPushEndpoint = "https://exp.host/--/api/v2/push/send";
    private const string DeliveryApprovalRequestType = "DeliveryApprovalRequested";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task SendDeliveryApprovalRequestAsync(
        Guid residentUserId,
        Guid deliveryRequestId,
        string lockerCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendCoreAsync(
                residentUserId,
                deliveryRequestId,
                lockerCode,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to create Expo push notification for delivery request {DeliveryRequestId}.",
                deliveryRequestId);
        }
    }

    private async Task SendCoreAsync(
        Guid residentUserId,
        Guid deliveryRequestId,
        string lockerCode,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Notification notification = new()
        {
            Id = Guid.NewGuid(),
            UserId = residentUserId,
            Type = DeliveryApprovalRequestType,
            Channel = NotificationChannel.Push,
            Title = "Có yêu cầu giao hàng mới",
            Message = $"Shipper đang yêu cầu gửi kiện hàng vào tủ {lockerCode}.",
            DeliveryStatus = NotificationDeliveryStatus.Pending,
            CreatedAt = now
        };
        dbContext.Notifications.Add(notification);

        List<DeviceInstallation> devices = await dbContext.DeviceInstallations
            .Where(device => device.UserId == residentUserId && device.IsActive)
            .OrderBy(device => device.CreatedAt)
            .ToListAsync(cancellationToken);

        if (devices.Count == 0)
        {
            notification.DeliveryStatus = NotificationDeliveryStatus.Failed;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var messages = devices.Select(device => new
            {
                to = device.ExpoPushToken,
                title = notification.Title,
                body = notification.Message,
                sound = "default",
                data = new
                {
                    type = "delivery_request",
                    deliveryRequestId
                }
            });

            using HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
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
                    devices[index].UpdatedAt = DateTimeOffset.UtcNow;
                }
            }

            notification.DeliveryStatus = sent
                ? NotificationDeliveryStatus.Sent
                : NotificationDeliveryStatus.Failed;
            notification.SentAt = sent ? DateTimeOffset.UtcNow : null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            notification.DeliveryStatus = NotificationDeliveryStatus.Failed;
            logger.LogWarning(
                exception,
                "Failed to send Expo push notification for delivery request {DeliveryRequestId}.",
                deliveryRequestId);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static bool IsDeviceNotRegistered(JsonElement ticket) =>
        ticket.TryGetProperty("details", out JsonElement details) &&
        details.TryGetProperty("error", out JsonElement error) &&
        error.GetString() == "DeviceNotRegistered";
}
