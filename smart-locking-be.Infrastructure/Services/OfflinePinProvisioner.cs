using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

// Cấp PIN cho cả đơn đã gửi online, để khi đến nhận điện thoại không cần có Internet.
public sealed class OfflinePinProvisioner(ApplicationDbContext db, TimeProvider clock)
{
    public async Task EnsureAsync(CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        if (!await db.SystemPolicies.AnyAsync(x => x.IsActive && x.EnableBluetoothUnlock && x.EffectiveFrom <= now &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo > now), ct)) return;
        var operationalCompartments = db.OperationalCompartments();
        var parcels = await db.Parcels.Include(x => x.DeliveryRequest).ThenInclude(x => x.ResidentProfile)
            .Include(x => x.DeliveryRequest).ThenInclude(x => x.AllocatedCompartment)
            .Include(x => x.DeliveryRequest).ThenInclude(x => x.Locker)
            .Where(x => (x.Status == ParcelStatus.Stored || x.Status == ParcelStatus.Overdue) && x.MaxStorageUntil > now &&
                operationalCompartments.Any(available => available.Id == x.DeliveryRequest.AllocatedCompartmentId) &&
                !db.OfflineLockerPins.Any(p => p.ParcelId == x.Id)).ToListAsync(ct);
        foreach (Parcel parcel in parcels)
        {
            DeliveryRequest delivery = parcel.DeliveryRequest;
            if (delivery.ResidentProfile is null || delivery.AllocatedCompartment is null || delivery.RecipientPhoneSnapshot is null ||
                delivery.AllocatedCompartment.HardwareChannel is < 1 or > 4 ||
                delivery.AllocatedCompartment.OperationalStatus != LockerCompartmentOperationalStatus.Operational ||
                delivery.Locker.OperationalStatus != LockerOperationalStatus.Operational) continue;
            string phone = delivery.RecipientPhoneSnapshot;
            if (phone.StartsWith("+84", StringComparison.Ordinal)) phone = "0" + phone[3..];
            if (phone.Length != 10 || phone[0] != '0' || phone.Any(c => c < '0' || c > '9')) continue;
            string pin;
            do { pin = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture); }
            while (await db.OfflineLockerPins.AnyAsync(x => x.LockerId == delivery.LockerId && x.ConsumedAt == null && x.PinCode == pin, ct) ||
                db.OfflineLockerPins.Local.Any(x => x.LockerId == delivery.LockerId && x.ConsumedAt == null && x.PinCode == pin));
            db.OfflineLockerPins.Add(new()
            {
                ParcelId = parcel.Id,
                DeliveryId = delivery.Id,
                LockerId = delivery.LockerId,
                RecipientPhone = phone,
                PinCode = pin,
                CreatedAt = now
            });
        }
        await db.SaveChangesAsync(ct);
    }

    internal static void AddPinAndNotifications(ApplicationDbContext db, Parcel parcel, Guid userId, string phone,
        string pin, string lockerCode, string compartmentCode, DateTimeOffset now)
    {
        db.OfflineLockerPins.Add(new()
        {
            ParcelId = parcel.Id,
            DeliveryId = parcel.DeliveryRequestId,
            LockerId = parcel.DeliveryRequest.LockerId,
            RecipientPhone = phone,
            PinCode = pin,
            CreatedAt = now,
            ProvisionedAt = now
        });
        AddNotifications(db, parcel, userId, pin, lockerCode, compartmentCode, now);
    }

    internal static void AddNotifications(ApplicationDbContext db, Parcel parcel, Guid userId, string pin,
        string lockerCode, string compartmentCode, DateTimeOffset now)
    {
        string message = $"Có hàng tại tủ {lockerCode}, ngăn {compartmentCode}. PIN nhận hàng: {pin}. Không chia sẻ mã này.";
        foreach (NotificationChannel channel in new[] { NotificationChannel.InApp, NotificationChannel.Push })
            db.Notifications.Add(new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DeliveryRequestId = parcel.DeliveryRequestId,
                ParcelId = parcel.Id,
                Type = "OfflinePinParcelStored",
                Channel = channel,
                Title = "Hàng đã vào tủ",
                Message = message,
                DeliveryStatus = channel == NotificationChannel.InApp ? NotificationDeliveryStatus.Sent : NotificationDeliveryStatus.Pending,
                SentAt = channel == NotificationChannel.InApp ? now : null,
                CreatedAt = now
            });
    }
}
