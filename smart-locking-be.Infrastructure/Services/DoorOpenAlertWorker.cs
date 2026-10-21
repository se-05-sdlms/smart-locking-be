using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class DoorOpenAlertWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DoorOpenAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int seconds = configuration.GetValue<int?>("LockerMonitoring:DoorOpenAlertAfterSeconds") ?? 120;
        if (seconds < 10) throw new InvalidOperationException("LockerMonitoring:DoorOpenAlertAfterSeconds must be at least 10.");

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                await CheckAsync(scope.ServiceProvider, TimeSpan.FromSeconds(seconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to check doors left open.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static async Task CheckAsync(IServiceProvider services, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        var notifier = services.GetRequiredService<IOperationsRealtimeNotifier>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<LockerCompartment> openDoors = await dbContext.LockerCompartments
            .AsNoTracking().Include(item => item.Locker)
            .Where(item => item.DoorStatus == DoorStatus.Open)
            .ToListAsync(cancellationToken);

        foreach (LockerCompartment compartment in openDoors)
        {
            LockerEvent? opened = await dbContext.LockerEvents.AsNoTracking()
                .Where(item => item.LockerCompartmentId == compartment.Id && item.EventType == LockerEventType.DoorChanged)
                .OrderByDescending(item => item.OccurredAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (opened?.NewValue != nameof(DoorStatus.Open) || opened.OccurredAt > now - timeout) continue;

            bool alerted = await dbContext.Incidents.AnyAsync(item =>
                item.LockerCompartmentId == compartment.Id && item.Type == "DoorOpenTooLong" &&
                item.CreatedAt >= opened.ReceivedAt, cancellationToken);
            if (alerted) continue;

            LockerAccessEvent? access = await dbContext.LockerAccessEvents.AsNoTracking()
                .Where(item => item.LockerCompartmentId == compartment.Id &&
                    item.Result == LockerAccessResult.Succeeded &&
                    item.OccurredAt <= opened.ReceivedAt && item.OccurredAt >= opened.ReceivedAt.AddMinutes(-2))
                .OrderByDescending(item => item.OccurredAt)
                .FirstOrDefaultAsync(cancellationToken);
            Guid? operatorId = await dbContext.OperatorAssignments
                .Where(item => item.LockerId == compartment.LockerId && item.RevokedAt == null &&
                    item.OperatorUser.Status == UserStatus.Active)
                .Select(item => (Guid?)item.OperatorUserId)
                .FirstOrDefaultAsync(cancellationToken);
            string title = $"Door {compartment.Code} left open";
            Incident incident = new()
            {
                Id = Guid.NewGuid(),
                LockerId = compartment.LockerId,
                LockerCompartmentId = compartment.Id,
                DeliveryRequestId = access?.DeliveryRequestId,
                ParcelId = access?.ParcelId,
                ReturnRequestId = access?.ReturnRequestId,
                AssignedOperatorUserId = operatorId,
                Type = "DoorOpenTooLong",
                Source = IncidentSource.System,
                Status = IncidentStatus.Open,
                Title = title,
                Description = $"Door has been open since {opened.OccurredAt:O}.",
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.Incidents.Add(incident);
            dbContext.LockerEvents.Add(new LockerEvent
            {
                Id = Guid.NewGuid(),
                LockerId = compartment.LockerId,
                LockerCompartmentId = compartment.Id,
                EventType = LockerEventType.DeviceAlert,
                Severity = LockerEventSeverity.Warning,
                Reason = "DoorOpenTooLong",
                Details = title,
                OccurredAt = now,
                ReceivedAt = now
            });

            List<Guid> recipients = operatorId.HasValue ? [operatorId.Value] : await dbContext.Users
                .Where(item => item.Role == UserRole.Administrator && item.Status == UserStatus.Active)
                .Select(item => item.Id).ToListAsync(cancellationToken);
            foreach (Guid userId in recipients)
                dbContext.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    IncidentId = incident.Id,
                    Type = "DoorOpenTooLong",
                    Channel = NotificationChannel.InApp,
                    Title = title,
                    Message = $"Locker {compartment.Locker.Code}: {title}.",
                    DeliveryStatus = NotificationDeliveryStatus.Sent,
                    SentAt = now,
                    CreatedAt = now
                });

            await dbContext.SaveChangesAsync(cancellationToken);
            RealtimeEvent message = new("LockerDeviceAlert", incident.Id, compartment.LockerId,
                "DoorOpenTooLong", title, now);
            await notifier.PublishToLockerAsync(compartment.LockerId, message, cancellationToken);
            foreach (Guid userId in recipients)
                await notifier.PublishToUserAsync(userId, message, cancellationToken);
        }
    }
}
