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

        string? blockedReason = GetBlockedReason(compartment, request.AccessType);
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

    private static string? GetBlockedReason(LockerCompartment compartment, LockerAccessType accessType)
    {
        if (compartment.Locker.ConnectionStatus != LockerConnectionStatus.Online)
        {
            return "Locker hiện không trực tuyến.";
        }
        if (accessType is not LockerAccessType.OperatorEmergency and not LockerAccessType.Maintenance)
        {
            if (compartment.Locker.OperationalStatus != LockerOperationalStatus.Operational)
            {
                return "Locker hiện không sẵn sàng vận hành.";
            }
            if (compartment.OperationalStatus != LockerCompartmentOperationalStatus.Operational)
            {
                return "Ngăn locker hiện không sẵn sàng vận hành.";
            }
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
