using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

        await using IDbContextTransaction? transaction = request.AccessType == LockerAccessType.ShipperDropOff &&
            dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (request.AccessType == LockerAccessType.ShipperDropOff)
        {
            if (transaction is not null)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"DeliveryRequest\" WHERE \"Id\" = {request.DeliveryRequestId!.Value} FOR UPDATE",
                    cancellationToken);
            }
            DeliveryRequest? delivery = await dbContext.DeliveryRequests.AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == request.DeliveryRequestId, cancellationToken);
            if (delivery is null || delivery.Status != DeliveryRequestStatus.Allocated ||
                delivery.AllocatedCompartmentId != request.LockerCompartmentId ||
                delivery.ReservationExpiresAt is null || delivery.ReservationExpiresAt <= timeProvider.GetUtcNow())
            {
                throw new InvalidOperationException("Yêu cầu không còn giữ ngăn tủ hợp lệ.");
            }
        }

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
            Result = LockerAccessResult.Succeeded,
        };

        string? blockedReason = GetBlockedReason(compartment);
        if (blockedReason is not null)
        {
            accessEvent.Result = LockerAccessResult.Blocked;
            accessEvent.FailureReason = blockedReason;
            accessEvent.CompletedAt = now;
        }

        // Persist before publishing: the device can acknowledge or close the door immediately.
        dbContext.LockerAccessEvents.Add(accessEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        if (blockedReason is null)
        {
            try
            {
                await commandDispatcher.DispatchUnlockAsync(
                    new LockerUnlockCommand(
                        accessEvent.Id,
                        compartment.Locker.DeviceIdentifier,
                        compartment.HardwareChannel),
                    cancellationToken);
            }
            catch (LockerCommandDeliveryUnknownException exception)
            {
                // Delivery may have happened; only the device can settle this access safely.
                return Map(accessEvent, compartment) with { FailureReason = exception.Message };
            }
            catch (Exception exception)
            {
                if (dbContext.Database.IsRelational())
                {
                    DateTimeOffset failedAt = timeProvider.GetUtcNow();
                    await dbContext.LockerAccessEvents
                        .Where(e => e.Id == accessEvent.Id && e.CompletedAt == null)
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(e => e.Result, LockerAccessResult.Failed)
                            .SetProperty(e => e.FailureReason, "Không thể gửi lệnh mở ngăn tới locker.")
                            .SetProperty(e => e.CompletedAt, failedAt), CancellationToken.None);
                    await dbContext.Entry(accessEvent).ReloadAsync(CancellationToken.None);
                }
                else
                {
                    accessEvent.Result = LockerAccessResult.Failed;
                    accessEvent.FailureReason = "Không thể gửi lệnh mở ngăn tới locker.";
                    accessEvent.CompletedAt = timeProvider.GetUtcNow();
                    await dbContext.SaveChangesAsync(CancellationToken.None);
                }
                if (exception is OperationCanceledException) throw;
            }
        }

        return Map(accessEvent, compartment);
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
