using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

// Chỉ gửi push sau khi backend đã nhận nhật ký; thông báo trong app được lưu riêng.
public sealed class OfflinePinNotificationWorker(IServiceScopeFactory scopes,
    ILogger<OfflinePinNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchAsync(stoppingToken); }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Offline PIN notification dispatch failed; pending notifications will be retried."); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task DispatchAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Các yêu cầu thu hồi đã được ghi trước khi gửi; thử lại cùng ID sau mất mạng/restart.
        await scope.ServiceProvider.GetRequiredService<ICompartmentResetService>().RetryPendingTransfersAsync(ct);
        var notifications = await db.Notifications.Include(x => x.User).Include(x => x.Parcel).Where(x => (x.Type == "OfflinePinParcelStored" || ((x.Type == "ReturnDeposited" || x.Type == "ReturnPickedUp" || x.Type == "ParcelStored" || x.Type == "ParcelTransferred") && (x.ReturnRequestId != null || x.ParcelId != null) && x.Channel == NotificationChannel.Push)) &&
            x.Channel == NotificationChannel.Push &&
            x.DeliveryStatus != NotificationDeliveryStatus.Sent).OrderBy(x => x.CreatedAt).Take(50).ToListAsync(ct);
        foreach (var notification in notifications)
        {
            if (notification.Type == "OfflinePinParcelStored" && notification.Parcel?.Status is ParcelStatus.Retrieved or ParcelStatus.Removed)
                notification.Message = "Kiện hàng đã được nhận hoặc chuyển khỏi ngăn. PIN cũ không còn hiệu lực.";
            await scope.ServiceProvider.GetRequiredService<IPushNotificationService>().TrySendAsync(notification.Id, ct);
        }
    }
}
