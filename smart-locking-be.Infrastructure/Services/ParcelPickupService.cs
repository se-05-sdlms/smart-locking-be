using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ParcelPickupService(
    ApplicationDbContext dbContext,
    ILockerAccessService lockerAccessService,
    TimeProvider timeProvider) : IParcelPickupService
{
    public async Task<PickupUnlockResponse> UnlockAsync(
        Guid residentUserId,
        Guid parcelId,
        string? ipAddress,
        string? deviceContext,
        CancellationToken cancellationToken = default)
    {
        Parcel parcel = await dbContext.Parcels
            .Include(item => item.OverdueCharge)
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.ResidentProfile)
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.SystemPolicy)
            .SingleOrDefaultAsync(item => item.Id == parcelId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy bưu kiện.");

        if (parcel.DeliveryRequest.ResidentProfile?.UserId != residentUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền mở ngăn chứa bưu kiện này.");
        }
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
        {
            throw new InvalidOperationException("Bưu kiện không còn ở trạng thái có thể nhận.");
        }
        if (parcel.OverdueCharge?.Status == OverdueChargeStatus.Outstanding)
        {
            throw new InvalidOperationException("Cần thanh toán phí quá hạn trước khi nhận bưu kiện.");
        }
        if (!parcel.DeliveryRequest.SystemPolicy.EnableRemoteUnlock)
        {
            throw new InvalidOperationException("Chính sách hiện tại không cho phép mở tủ từ ứng dụng.");
        }
        if (!parcel.DeliveryRequest.AllocatedCompartmentId.HasValue)
        {
            throw new InvalidOperationException("Bưu kiện chưa được gắn với ngăn locker.");
        }

        OpenLockerResponse access = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            parcel.DeliveryRequest.LockerId,
            parcel.DeliveryRequest.AllocatedCompartmentId.Value,
            residentUserId,
            null,
            parcel.Id,
            null,
            LockerAccessType.ResidentPickup,
            LockerAccessMethod.RemoteApp,
            ipAddress,
            deviceContext), cancellationToken);

        return new PickupUnlockResponse(
            parcel.Id,
            access.AccessEventId,
            access.Result,
            access.FailureReason,
            access.OccurredAt);
    }

    public async Task<PickupConfirmationResponse> ConfirmPickupAsync(
        Guid residentUserId,
        Guid lockerAccessEventId,
        CancellationToken cancellationToken = default)
    {
        LockerAccessEvent accessEvent = await dbContext.LockerAccessEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == lockerAccessEventId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy lượt mở ngăn lấy hàng.");

        if (accessEvent.AccessType != LockerAccessType.ResidentPickup ||
            accessEvent.Result != LockerAccessResult.Succeeded ||
            !accessEvent.ParcelId.HasValue)
        {
            throw new InvalidOperationException("Lượt mở ngăn không hợp lệ để xác nhận lấy hàng.");
        }
        if (accessEvent.UserId != residentUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xác nhận lượt lấy hàng này.");
        }

        Parcel parcel = await dbContext.Parcels
            .Include(item => item.DeliveryRequest)
            .SingleOrDefaultAsync(item => item.Id == accessEvent.ParcelId.Value, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy bưu kiện.");

        if (parcel.DeliveryRequest.AllocatedCompartmentId != accessEvent.LockerCompartmentId)
        {
            throw new InvalidOperationException("Lượt mở ngăn không khớp với vị trí bưu kiện.");
        }
        DoorStatus doorStatus = await dbContext.LockerCompartments
            .Where(item => item.Id == accessEvent.LockerCompartmentId)
            .Select(item => item.DoorStatus)
            .SingleAsync(cancellationToken);
        if (doorStatus != DoorStatus.Closed)
        {
            throw new InvalidOperationException("Chưa thể hoàn tất nhận hàng vì cửa ngăn chưa đóng.");
        }
        if (parcel.Status == ParcelStatus.Retrieved && parcel.RetrievedAt.HasValue)
        {
            return new PickupConfirmationResponse(parcel.Id, parcel.Status, parcel.RetrievedAt.Value);
        }
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
        {
            throw new InvalidOperationException("Bưu kiện không còn ở trạng thái có thể xác nhận lấy hàng.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        ParcelStatus previousStatus = parcel.Status;
        parcel.Status = ParcelStatus.Retrieved;
        parcel.RetrievedAt = now;
        parcel.UpdatedAt = now;
        parcel.DeliveryRequest.CompartmentReleasedAt = now;
        parcel.DeliveryRequest.UpdatedAt = now;
        dbContext.ParcelStatusHistories.Add(new ParcelStatusHistory
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            FromStatus = previousStatus,
            ToStatus = ParcelStatus.Retrieved,
            Reason = "Cư dân đã lấy bưu kiện và cửa ngăn locker đã đóng.",
            ChangedByUserId = accessEvent.UserId,
            ChangedAt = now,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new PickupConfirmationResponse(parcel.Id, parcel.Status, now);
    }
}
