using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class OfflineLockerEventConfiguration() : BaseConfiguration<OfflineLockerEvent>(x => x.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<OfflineLockerEvent> builder)
    {
        builder.Property(x => x.DeviceIdentifier).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(1).IsRequired();
        builder.Property(x => x.RecipientPhone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.PinCode).HasMaxLength(6);
        builder.HasIndex(x => new { x.DeviceIdentifier, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.DeviceIdentifier, x.DeliveryId, x.Kind });
        builder.HasIndex(x => new { x.Status, x.ReceivedAt });
    }
}

public sealed class OfflineLockerCursorConfiguration() : BaseConfiguration<OfflineLockerCursor>(x => x.DeviceIdentifier)
{
    protected override void ConfigureEntity(EntityTypeBuilder<OfflineLockerCursor> builder)
    {
        builder.Property(x => x.DeviceIdentifier).HasMaxLength(100);
        builder.Property(x => x.LastSequence).IsConcurrencyToken();
    }
}
