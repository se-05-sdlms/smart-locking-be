using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class LockerDeviceEventHandler(
    ApplicationDbContext dbContext,
    IParcelService parcelService,
    IDeliveryRequestService deliveryRequestService,
    TimeProvider timeProvider,
    ILogger<LockerDeviceEventHandler> logger) : ILockerDeviceEventHandler
{
    public async Task HandleCommandAckAsync(string deviceId, Guid commandId, bool ok, CancellationToken ct)
    {
        LockerAccessEvent? access = await dbContext.LockerAccessEvents.SingleOrDefaultAsync(
            e => e.Id == commandId && e.Locker.DeviceIdentifier == deviceId, ct);
        if (access is null || access.CompletedAt.HasValue || ok)
        {
            return;
        }

        access.Result = LockerAccessResult.Failed;
        access.FailureReason = "Thiết bị từ chối lệnh mở ngăn.";
        access.CompletedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task HandleDoorChangedAsync(
        string deviceId, int hardwareChannel, DoorStatus state, DateTimeOffset at, CancellationToken ct)
    {
        LockerCompartment? compartment = await dbContext.LockerCompartments.SingleOrDefaultAsync(
            c => c.Locker.DeviceIdentifier == deviceId && c.HardwareChannel == hardwareChannel, ct);
        if (compartment is null)
        {
            logger.LogWarning("Không tìm thấy ngăn {DeviceId}/{HardwareChannel}.", deviceId, hardwareChannel);
            return;
        }

        DoorStatus previous = compartment.DoorStatus;
        compartment.DoorStatus = state;
        compartment.UpdatedAt = timeProvider.GetUtcNow();
        dbContext.LockerEvents.Add(new LockerEvent
        {
            Id = Guid.NewGuid(),
            LockerId = compartment.LockerId,
            LockerCompartmentId = compartment.Id,
            EventType = LockerEventType.DoorChanged,
            PreviousValue = previous.ToString(),
            NewValue = state.ToString(),
            Severity = LockerEventSeverity.Info,
            OccurredAt = at,
            ReceivedAt = timeProvider.GetUtcNow(),
        });

        if (state == DoorStatus.Closed && previous == DoorStatus.Open)
        {
            LockerAccessEvent? access = await dbContext.LockerAccessEvents
                .Where(e => e.LockerCompartmentId == compartment.Id &&
                            e.Result == LockerAccessResult.Succeeded && e.CompletedAt == null && e.OccurredAt <= at)
                .OrderByDescending(e => e.OccurredAt)
                .FirstOrDefaultAsync(ct);
            if (access is null)
            {
                logger.LogInformation("Cửa ngăn {CompartmentId} đóng mà không có lượt truy cập đang chờ.", compartment.Id);
            }
            else
            {
                switch (access.AccessType)
                {
                    case LockerAccessType.ShipperDropOff when access.DeliveryRequestId.HasValue:
                        await deliveryRequestService.FinalizeDropOffAsync(access.DeliveryRequestId.Value, at, ct);
                        access.CompletedAt = at;
                        break;
                    case LockerAccessType.ResidentPickup when access.ParcelId.HasValue:
                        await parcelService.FinalizeRetrievalAsync(access.ParcelId.Value, access.UserId, at, ct);
                        access.CompletedAt = at;
                        break;
                    default:
                        logger.LogWarning("Chưa hỗ trợ xác nhận đóng cửa cho {AccessType}.", access.AccessType);
                        break;
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task HandleConnectionChangedAsync(string deviceId, bool online, CancellationToken ct)
    {
        Locker? locker = await dbContext.Lockers.SingleOrDefaultAsync(l => l.DeviceIdentifier == deviceId, ct);
        if (locker is null)
        {
            logger.LogWarning("Không tìm thấy locker của thiết bị {DeviceId}.", deviceId);
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        LockerConnectionStatus previous = locker.ConnectionStatus;
        locker.ConnectionStatus = online ? LockerConnectionStatus.Online : LockerConnectionStatus.Offline;
        locker.UpdatedAt = now;
        if (online) locker.LastSeenAt = now;
        dbContext.LockerEvents.Add(new LockerEvent
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            EventType = LockerEventType.ConnectionChanged,
            PreviousValue = previous.ToString(),
            NewValue = locker.ConnectionStatus.ToString(),
            Severity = LockerEventSeverity.Info,
            OccurredAt = now,
            ReceivedAt = now,
        });
        await dbContext.SaveChangesAsync(ct);
    }
}
