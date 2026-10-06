using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class LockerAccessService(
    ApplicationDbContext dbContext,
    ILockerCommandDispatcher commandDispatcher,
    TimeProvider timeProvider) : ILockerAccessService
{
    public async Task<OpenLockerResponse> OpenAsync(
        OpenLockerRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(request);

        LockerCompartment compartment = await dbContext.LockerCompartments
            .Include(item => item.Locker)
            .SingleOrDefaultAsync(item => item.Id == request.LockerCompartmentId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy ngăn locker.");

        if (compartment.LockerId != request.LockerId)
        {
            throw new ArgumentException("Ngăn locker không thuộc locker được yêu cầu.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        LockerAccessEvent accessEvent = new()
        {
            Id = Guid.NewGuid(),
            LockerId = request.LockerId,
            LockerCompartmentId = request.LockerCompartmentId,
            UserId = request.UserId,
            DeliveryRequestId = request.DeliveryRequestId,
            ParcelId = request.ParcelId,
            ReturnRequestId = request.ReturnRequestId,
            AccessType = request.AccessType,
            AccessMethod = request.AccessMethod,
            IpAddress = TrimToNull(request.IpAddress),
            DeviceContext = TrimToNull(request.DeviceContext),
            OccurredAt = now,
        };

        string? blockedReason = GetBlockedReason(compartment);
        if (blockedReason is not null)
        {
            accessEvent.Result = LockerAccessResult.Blocked;
            accessEvent.FailureReason = blockedReason;
        }
        else
        {
            try
            {
                await commandDispatcher.DispatchUnlockAsync(
                    new LockerUnlockCommand(
                        accessEvent.Id,
                        compartment.Locker.DeviceIdentifier,
                        compartment.HardwareChannel),
                    cancellationToken);
                accessEvent.Result = LockerAccessResult.Succeeded;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                accessEvent.Result = LockerAccessResult.Failed;
                accessEvent.FailureReason = "Không thể gửi lệnh mở ngăn tới locker.";
            }
        }

        dbContext.LockerAccessEvents.Add(accessEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(accessEvent, compartment);
    }

    public async Task<ConfigureCompartmentPinResponse> ConfigurePinAsync(
        Guid lockerId,
        Guid compartmentId,
        ConfigureCompartmentPinRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PinCode) || request.PinCode.Trim().Length < 4)
        {
            throw new ArgumentException("Mã PIN phải có ít nhất 4 chữ số.");
        }

        string pin = request.PinCode.Trim();

        var compartment = await dbContext.LockerCompartments
            .Include(c => c.Locker)
            .SingleOrDefaultAsync(c => c.Id == compartmentId && c.LockerId == lockerId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy ngăn locker.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        bool dispatched = false;

        if (compartment.Locker.ConnectionStatus == LockerConnectionStatus.Online)
        {
            try
            {
                await commandDispatcher.DispatchPinConfigAsync(
                    new LockerPinConfigCommand(
                        Guid.NewGuid(),
                        compartment.Locker.DeviceIdentifier,
                        compartment.HardwareChannel,
                        pin),
                    cancellationToken);
                dispatched = true;
            }
            catch
            {
                dispatched = false;
            }
        }

        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = null,
            Action = "LockerCompartment.ConfigurePin",
            EntityType = "LockerCompartment",
            EntityId = compartment.Id,
            Result = AuditLogResult.Succeeded,
            Details = $"Cấu hình mã PIN mới cho ngăn '{compartment.Code}' thuộc tủ '{compartment.Locker.Code}' (Kênh {compartment.HardwareChannel})",
            OccurredAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ConfigureCompartmentPinResponse(
            compartment.LockerId,
            compartment.Id,
            compartment.Locker.DeviceIdentifier,
            compartment.HardwareChannel,
            pin,
            dispatched,
            now);
    }

    public async Task<SyncOfflineAccessResponse> SyncOfflineEventsAsync(
        SyncOfflineAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        int total = request.Events.Count;
        int success = 0;
        var details = new List<string>();

        if (total == 0)
        {
            return new SyncOfflineAccessResponse(0, 0, details);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (var ev in request.Events)
        {
            var locker = await dbContext.Lockers
                .Include(l => l.Compartments)
                .FirstOrDefaultAsync(l => l.DeviceIdentifier == ev.DeviceIdentifier, cancellationToken);

            if (locker is null)
            {
                details.Add($"Không tìm thấy tủ với DeviceIdentifier '{ev.DeviceIdentifier}'.");
                continue;
            }

            var compartment = locker.Compartments.FirstOrDefault(c => c.HardwareChannel == ev.HardwareChannel);
            if (compartment is null)
            {
                details.Add($"Không tìm thấy ngăn kênh {ev.HardwareChannel} trên tủ '{locker.Code}'.");
                continue;
            }

            var accessEvent = new LockerAccessEvent
            {
                Id = Guid.NewGuid(),
                LockerId = locker.Id,
                LockerCompartmentId = compartment.Id,
                AccessType = LockerAccessType.ResidentPickup,
                AccessMethod = LockerAccessMethod.Bluetooth,
                Result = LockerAccessResult.Succeeded,
                DeviceContext = $"Offline BLE Sync (PIN: {ev.PinUsed})",
                OccurredAt = ev.OccurredAt
            };
            dbContext.LockerAccessEvents.Add(accessEvent);

            dbContext.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = null,
                Action = "OfflineUnlock.Bluetooth.Sync",
                EntityType = "LockerCompartment",
                EntityId = compartment.Id,
                Result = AuditLogResult.Succeeded,
                Details = $"Đồng bộ sự kiện mở tủ ngoại tuyến qua Bluetooth: Ngăn '{compartment.Code}', PIN: {ev.PinUsed}",
                OccurredAt = now
            });

            success++;
            details.Add($"Đã đồng bộ thành công sự kiện cho ngăn '{compartment.Code}' thuộc tủ '{locker.Code}'.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new SyncOfflineAccessResponse(total, success, details);
    }

    private static string? GetBlockedReason(LockerCompartment compartment)
    {
        if (compartment.Locker.OperationalStatus != LockerOperationalStatus.Operational)
        {
            return "Locker hiện không sẵn sàng vận hành.";
        }
        if (compartment.Locker.ConnectionStatus != LockerConnectionStatus.Online)
        {
            return "Locker hiện không trực tuyến.";
        }
        if (compartment.OperationalStatus != LockerCompartmentOperationalStatus.Operational)
        {
            return "Ngăn locker hiện không sẵn sàng vận hành.";
        }
        return null;
    }

    private static void ValidateContext(OpenLockerRequest request)
    {
        bool missingContext = request.AccessType switch
        {
            LockerAccessType.ShipperDropOff => !request.DeliveryRequestId.HasValue,
            LockerAccessType.ResidentPickup => !request.ParcelId.HasValue,
            LockerAccessType.ResidentReturnDropOff or LockerAccessType.ShipperReturnPickup =>
                !request.ReturnRequestId.HasValue,
            _ => false,
        };
        if (missingContext)
        {
            throw new ArgumentException("Thiếu định danh nghiệp vụ cho thao tác mở locker.");
        }
        if (request.AccessMethod == LockerAccessMethod.RemoteApp && !request.UserId.HasValue)
        {
            throw new ArgumentException("Mở locker từ ứng dụng yêu cầu người dùng đã xác thực.");
        }
        if (TrimToNull(request.IpAddress)?.Length > 45)
        {
            throw new ArgumentException("Địa chỉ IP không hợp lệ.");
        }
    }

    private static OpenLockerResponse Map(LockerAccessEvent accessEvent, LockerCompartment compartment) => new(
        accessEvent.Id,
        accessEvent.LockerId,
        accessEvent.LockerCompartmentId,
        compartment.Locker.DeviceIdentifier,
        compartment.HardwareChannel,
        accessEvent.Result,
        accessEvent.FailureReason,
        accessEvent.OccurredAt);

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
