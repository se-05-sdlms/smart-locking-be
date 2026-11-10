using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public static class ParcelRetrievalEligibility
{
    public static async Task<Parcel> LoadAsync(ApplicationDbContext db, Guid userId, Guid parcelId, DateTimeOffset now, CancellationToken ct)
    {
        Parcel parcel = await db.Parcels
            .Include(x => x.OverdueCharge)
            .Include(x => x.DeliveryRequest).ThenInclude(x => x.ResidentProfile!).ThenInclude(x => x.User)
            .Include(x => x.DeliveryRequest).ThenInclude(x => x.SystemPolicy)
            .Include(x => x.DeliveryRequest).ThenInclude(x => x.Locker)
            .Include(x => x.DeliveryRequest).ThenInclude(x => x.AllocatedCompartment)
            .SingleOrDefaultAsync(x => x.Id == parcelId, ct) ?? throw new NotFoundException(ParcelErrorCodes.NotFound, "Parcel not found.");
        ResidentProfile? resident = parcel.DeliveryRequest.ResidentProfile;
        if (resident?.UserId != userId)
            throw new ForbiddenException(ParcelErrorCodes.AccessDenied, "Kiện hàng không thuộc resident này.");
        if (resident.User.Status != UserStatus.Active || resident.User.Role != UserRole.Resident || parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue) ||
            now >= parcel.MaxStorageUntil)
            throw new ConflictException(ParcelErrorCodes.InvalidState, "Parcel cannot be retrieved.");
        // Dù worker tính phí chạy trễ, vẫn phải kiểm tra phí trước khi cho mở ngăn nhận hàng.
        if (parcel.OverdueCharge?.Status == OverdueChargeStatus.Outstanding ||
            (now >= parcel.PickupDueAt && parcel.DeliveryRequest.SystemPolicy.OverdueFeePerHour > 0 &&
             parcel.OverdueCharge?.Status != OverdueChargeStatus.Paid))
            throw new ConflictException(ParcelErrorCodes.PaymentRequired, "Pay the mandatory overdue fee before retrieval.");
        if (parcel.DeliveryRequest.AllocatedCompartment is null)
            throw new ConflictException(ParcelErrorCodes.InvalidState, "Parcel has no allocated compartment.");
        return parcel;
    }
}
