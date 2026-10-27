using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed record IncidentSlaRunResult(int Warned, int Escalated);

/// <summary>
/// SLA Escalation Monitor (SRS NSF-10, UC-16): cảnh báo ở 80% hạn và tự escalate incident quá hạn lên Administrator.
/// Mỗi incident chỉ bị escalate một lần: Status chuyển sang Escalated nên các lần chạy sau bỏ qua.
/// </summary>
public sealed class IncidentSlaService(
    ApplicationDbContext dbContext,
    IOptions<IncidentSlaOptions> options,
    TimeProvider timeProvider,
    IOperationsRealtimeNotifier? realtimeNotifier = null)
{
    internal const string AcknowledgeWarningType = "IncidentSlaAcknowledgeWarning";
    internal const string ResolveWarningType = "IncidentSlaResolveWarning";

    public static IncidentPriority GetPriority(string type) => type.ToLowerInvariant() switch
    {
        "locker" => IncidentPriority.Critical,
        "compartment" or "payment" or "retrieval" => IncidentPriority.High,
        "other" => IncidentPriority.Low,
        _ => IncidentPriority.Medium
    };

    public async Task<IncidentSlaRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        IncidentSlaOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return new IncidentSlaRunResult(0, 0);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        double warningFraction = Math.Clamp(settings.WarningPercent, 1, 99) / 100d;
        List<Incident> open = await dbContext.Incidents
            .Where(incident => incident.Status == IncidentStatus.Open || incident.Status == IncidentStatus.Investigating)
            .ToListAsync(cancellationToken);

        int warned = 0;
        int escalated = 0;
        foreach (Incident incident in open)
        {
            IncidentPriority priority = GetPriority(incident.Type);
            SlaTargets targets = settings.For(priority);
            TimeSpan sinceCreated = now - incident.CreatedAt;
            TimeSpan acknowledgeLimit = TimeSpan.FromMinutes(targets.AcknowledgeMinutes);
            TimeSpan resolveLimit = TimeSpan.FromMinutes(targets.ResolveMinutes);
            // Acknowledgement is only pending while nobody has picked the incident up.
            bool awaitingAcknowledge = incident.Status == IncidentStatus.Open && incident.AssignedOperatorUserId is null;

            string? breach = null;
            if (awaitingAcknowledge && sinceCreated >= acknowledgeLimit)
            {
                breach = $"Quá hạn tiếp nhận {targets.AcknowledgeMinutes} phút (mức {priority})";
            }
            else if (sinceCreated >= resolveLimit)
            {
                breach = $"Quá hạn giải quyết {targets.ResolveMinutes} phút (mức {priority})";
            }

            if (breach is not null)
            {
                await EscalateAsync(incident, breach, now, cancellationToken);
                escalated++;
                continue;
            }

            if (awaitingAcknowledge && sinceCreated >= acknowledgeLimit * warningFraction &&
                await WarnAsync(incident, AcknowledgeWarningType,
                    $"Incident '{incident.Title}' sắp quá hạn tiếp nhận ({priority}).", now, cancellationToken))
            {
                warned++;
            }
            else if (sinceCreated >= resolveLimit * warningFraction &&
                     await WarnAsync(incident, ResolveWarningType,
                         $"Incident '{incident.Title}' sắp quá hạn giải quyết ({priority}).", now, cancellationToken))
            {
                warned++;
            }
        }

        return new IncidentSlaRunResult(warned, escalated);
    }

    private async Task EscalateAsync(Incident incident, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        IncidentStatus previous = incident.Status;
        incident.Status = IncidentStatus.Escalated;
        incident.EscalatedAt = now;
        incident.UpdatedAt = now;
        dbContext.IncidentActions.Add(new IncidentAction
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ActionByUserId = null,
            ActionType = "StatusChanged",
            FromStatus = previous,
            ToStatus = IncidentStatus.Escalated,
            Notes = $"Tự động escalate: {reason}",
            CreatedAt = now
        });
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = null,
            Action = "Incident.SlaEscalated",
            EntityType = nameof(Incident),
            EntityId = incident.Id,
            Result = AuditLogResult.Succeeded,
            Details = reason,
            OccurredAt = now
        });

        List<Guid> administratorIds = await dbContext.Users
            .Where(user => user.Role == UserRole.Administrator && user.Status == UserStatus.Active)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        foreach (Guid administratorId in administratorIds)
        {
            dbContext.Notifications.Add(CreateNotification(administratorId, incident, "IncidentEscalated",
                "Incident quá hạn xử lý", $"{incident.Title}. {reason}.", now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await PublishAsync(incident, cancellationToken);
    }

    /// <summary>Gửi cảnh báo sớm một lần cho mỗi mốc; trả false nếu đã gửi hoặc không có ai để báo.</summary>
    private async Task<bool> WarnAsync(Incident incident, string type, string message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        bool alreadyWarned = await dbContext.Notifications.AnyAsync(
            item => item.IncidentId == incident.Id && item.Type == type, cancellationToken);
        if (alreadyWarned)
        {
            return false;
        }

        List<Guid> recipients = [];
        if (incident.AssignedOperatorUserId.HasValue)
        {
            recipients.Add(incident.AssignedOperatorUserId.Value);
        }
        else if (incident.LockerId.HasValue)
        {
            recipients = await dbContext.OperatorAssignments
                .Where(assignment => assignment.LockerId == incident.LockerId && assignment.RevokedAt == null &&
                                     assignment.OperatorUser.Status == UserStatus.Active)
                .Select(assignment => assignment.OperatorUserId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        if (recipients.Count == 0)
        {
            recipients = await dbContext.Users
                .Where(user => user.Role == UserRole.Administrator && user.Status == UserStatus.Active)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
        }

        if (recipients.Count == 0)
        {
            return false;
        }

        foreach (Guid recipient in recipients)
        {
            dbContext.Notifications.Add(CreateNotification(recipient, incident, type, "Incident sắp quá hạn", message, now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task PublishAsync(Incident incident, CancellationToken cancellationToken)
    {
        if (realtimeNotifier is null) return;
        RealtimeEvent message = new("IncidentUpdated", incident.Id, incident.LockerId,
            incident.Status.ToString(), incident.Title, incident.UpdatedAt);
        if (incident.LockerId.HasValue)
            await realtimeNotifier.PublishToLockerAsync(incident.LockerId.Value, message, cancellationToken);
        if (incident.ReporterUserId.HasValue)
            await realtimeNotifier.PublishToUserAsync(incident.ReporterUserId.Value, message, cancellationToken);
    }

    private static Notification CreateNotification(
        Guid userId, Incident incident, string type, string title, string message, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IncidentId = incident.Id,
            ParcelId = incident.ParcelId,
            PaymentTransactionId = incident.PaymentTransactionId,
            Type = type,
            Channel = NotificationChannel.InApp,
            Title = title,
            Message = message,
            DeliveryStatus = NotificationDeliveryStatus.Sent,
            SentAt = now,
            CreatedAt = now
        };
}
