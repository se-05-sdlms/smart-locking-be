using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class LockerAccessService(
    ApplicationDbContext dbContext,
    ILockerCommandDispatcher commandDispatcher,
    TimeProvider timeProvider,
    IBluetoothMessageCodec? protocol = null,
    IConfiguration? configuration = null,
    IBluetoothCommandSigner? commandSigner = null) : ILockerAccessService
{
    public async Task<OpenLockerResponse> OpenAsync(
        OpenLockerRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(request);
        await using var pickupTransaction = request.AccessType == LockerAccessType.ResidentPickup && request.ParcelId.HasValue
            ? await ParcelTransitionLock.AcquireAsync(dbContext, request.ParcelId.Value, cancellationToken) : null;
        if (request.AccessType == LockerAccessType.ResidentPickup && request.ParcelId.HasValue &&
            !await dbContext.Parcels.AnyAsync(item => item.Id == request.ParcelId.Value &&
                item.DeliveryRequest.LockerId == request.LockerId && item.DeliveryRequest.AllocatedCompartmentId == request.LockerCompartmentId &&
                (item.Status == ParcelStatus.Stored || item.Status == ParcelStatus.Overdue) && item.MaxStorageUntil > timeProvider.GetUtcNow(), cancellationToken))
            throw new ConflictException(ParcelErrorCodes.InvalidState, "Parcel is no longer available for resident pickup.");
        bool useBluetooth = request.AccessMethod == LockerAccessMethod.Bluetooth;
        BluetoothCommandResponse? bluetoothCommand = null;

        LockerCompartment compartment = await dbContext.LockerCompartments
            .Include(item => item.Locker)
            .SingleOrDefaultAsync(item => item.Id == request.LockerCompartmentId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.InvalidCompartment, "Locker compartment not found.");

        if (compartment.LockerId != request.LockerId)
        {
            throw new BadRequestException(LockerErrorCodes.InvalidCompartment, "Compartment does not belong to the requested locker.");
        }

        Guid? deliveryId = request.DeliveryRequestId;
        if (request.ParcelId.HasValue)
            deliveryId = await dbContext.Parcels.Where(x => x.Id == request.ParcelId.Value)
                .Select(x => (Guid?)x.DeliveryRequestId).SingleOrDefaultAsync(cancellationToken);
        bool monitoringEnabled = configuration?.GetValue<bool>("LockerMonitoring:Enabled") ?? false;
        // Mất kết nối máy chủ chỉ chặn đường MQTT; điện thoại vẫn được nhận lệnh để chuyển qua BLE.
        if (!useBluetooth && monitoringEnabled && compartment.Locker.ConnectionStatus != LockerConnectionStatus.Online)
        {
            throw new ConflictException(LockerErrorCodes.Offline, "Locker is offline or has not reported its connection status.");
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

        string? blockedReason = GetBlockedReason(compartment, useBluetooth, monitoringEnabled);
        if (blockedReason is not null)
        {
            accessEvent.Result = LockerAccessResult.Blocked;
            accessEvent.FailureReason = blockedReason;
        }
        else
        {
            try
            {
                // BLE trả lệnh về điện thoại; MQTT gửi cùng nội dung xuống tủ.
                string? workflowCommand = protocol is null ? null : await BuildCommandAsync(request, compartment, accessEvent.Id, protocol, cancellationToken);
                if (useBluetooth)
                {
                    if (workflowCommand is null) throw new InvalidOperationException("Chua cau hinh lenh Bluetooth.");
                    bluetoothCommand = new(compartment.Locker.DeviceIdentifier, compartment.HardwareChannel,
                        (commandSigner ?? throw new InvalidOperationException("Chưa cấu hình xác thực lệnh Bluetooth."))
                            .Sign(compartment.Locker.DeviceIdentifier, workflowCommand), now.AddMinutes(2));
                }
                else await commandDispatcher.DispatchUnlockAsync(
                    new LockerUnlockCommand(
                        accessEvent.Id,
                        compartment.Locker.DeviceIdentifier,
                        compartment.HardwareChannel, deliveryId,
                        request.AccessType is LockerAccessType.OperatorEmergency or LockerAccessType.Maintenance, workflowCommand),
                    cancellationToken);
                accessEvent.Result = LockerAccessResult.Succeeded;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                accessEvent.Result = LockerAccessResult.Failed;
                accessEvent.FailureReason = "Could not send the unlock command to the locker.";
            }
        }

        dbContext.LockerAccessEvents.Add(accessEvent);
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = request.UserId,
            Action = "Locker.UnlockCommand",
            EntityType = nameof(LockerAccessEvent),
            EntityId = accessEvent.Id,
            Result = accessEvent.Result == LockerAccessResult.Succeeded ? AuditLogResult.Succeeded : AuditLogResult.Failed,
            Details = $"LockerId={request.LockerId}; CompartmentId={request.LockerCompartmentId}; AccessType={request.AccessType}; Result={accessEvent.Result}",
            OccurredAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (pickupTransaction is not null) await pickupTransaction.CommitAsync(cancellationToken);
        return Map(accessEvent, compartment, bluetoothCommand);
    }

    private async Task<string?> BuildCommandAsync(OpenLockerRequest request, LockerCompartment compartment, Guid commandId,
        IBluetoothMessageCodec protocol, CancellationToken ct)
    {
        if (request.AccessType is LockerAccessType.OperatorEmergency or LockerAccessType.Maintenance) return null;
        string kind, phone, pin = "-";
        Guid subject;
        if (request.ReturnRequestId is { } returnId)
        {
            var item = await dbContext.ReturnRequests.Include(x => x.ResidentProfile).ThenInclude(x => x.User).SingleAsync(x => x.Id == returnId, ct);
            kind = request.AccessType == LockerAccessType.ResidentReturnDropOff ? "R" : "G";
            subject = item.Id; phone = item.ResidentProfile.User.PhoneNumber ?? ""; pin = item.ReturnCode;
        }
        else
        {
            Guid? id = request.DeliveryRequestId;
            if (request.ParcelId is { } parcelId) id = await dbContext.Parcels.Where(x => x.Id == parcelId).Select(x => (Guid?)x.DeliveryRequestId).SingleAsync(ct);
            var item = await dbContext.DeliveryRequests.SingleAsync(x => x.Id == id, ct);
            kind = request.AccessType == LockerAccessType.ShipperDropOff ? "D" : "P";
            subject = item.Id; phone = item.RecipientPhoneSnapshot ?? "";
        }
        phone = phone.StartsWith("+84", StringComparison.Ordinal) ? "0" + phone[3..] : phone;
        return protocol.CreateCommand(compartment.Locker.DeviceIdentifier, subject, compartment.HardwareChannel, kind, phone, pin,
            timeProvider.GetUtcNow().AddMinutes(2), commandId);
    }

    private static string? GetBlockedReason(LockerCompartment compartment, bool allowBluetooth, bool monitoringEnabled)
    {
        if (compartment.Locker.OperationalStatus != LockerOperationalStatus.Operational)
        {
            return "Locker is not operational.";
        }
        if (!allowBluetooth && monitoringEnabled && compartment.Locker.ConnectionStatus != LockerConnectionStatus.Online)
        {
            return "Locker is offline.";
        }
        if (compartment.OperationalStatus != LockerCompartmentOperationalStatus.Operational)
        {
            return "Locker compartment is not operational.";
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
            throw new BadRequestException(CommonErrorCodes.InvalidRequest, "A business identifier is required to open the locker.");
        }
        if (request.AccessMethod == LockerAccessMethod.RemoteApp && !request.UserId.HasValue)
        {
            throw new BadRequestException(AuthErrorCodes.AuthenticationRequired, "Opening a locker from the app requires an authenticated user.");
        }
        if (TrimToNull(request.IpAddress)?.Length > 45)
        {
            throw BadRequestException.For("ipAddress", "IP address is invalid.");
        }
    }

    private static OpenLockerResponse Map(LockerAccessEvent accessEvent, LockerCompartment compartment, BluetoothCommandResponse? bluetoothCommand) => new(
        accessEvent.Id,
        accessEvent.LockerId,
        accessEvent.LockerCompartmentId,
        compartment.Locker.DeviceIdentifier,
        compartment.HardwareChannel,
        accessEvent.Result,
        accessEvent.FailureReason,
        accessEvent.OccurredAt, bluetoothCommand);

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
