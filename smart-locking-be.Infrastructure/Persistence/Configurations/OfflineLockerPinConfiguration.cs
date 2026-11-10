using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class OfflineLockerPinConfiguration() : BaseConfiguration<OfflineLockerPin>(x => x.ParcelId)
{
    protected override void ConfigureEntity(EntityTypeBuilder<OfflineLockerPin> builder)
    {
        builder.Property(x => x.RecipientPhone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.PinCode).HasMaxLength(6).IsRequired();
        builder.Property(x => x.ProvisionedAt).IsConcurrencyToken();
        builder.HasIndex(x => x.DeliveryId).IsUnique();
        builder.HasOne<Parcel>().WithOne().HasForeignKey<OfflineLockerPin>(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Locker>().WithMany().HasForeignKey(x => x.LockerId).OnDelete(DeleteBehavior.Restrict);
    }
}
