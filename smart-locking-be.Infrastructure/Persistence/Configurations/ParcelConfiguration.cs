using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class ParcelConfiguration() : BaseConfiguration<Parcel>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<Parcel> builder)
    {
        Varchar(builder.Property(entity => entity.ParcelCode)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Status)).IsRequired();
        Text(builder.Property(entity => entity.RemovalReason));

        builder.HasOne(entity => entity.DeliveryRequest)
            .WithOne(request => request.Parcel)
            .HasForeignKey<Parcel>(entity => entity.DeliveryRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.RemovedByUser)
            .WithMany(user => user.RemovedParcels)
            .HasForeignKey(entity => entity.RemovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.DeliveryRequestId)
            .HasDatabaseName("UX_Parcel_DeliveryRequestId")
            .IsUnique();
        builder.HasIndex(entity => entity.ParcelCode)
            .HasDatabaseName("UX_Parcel_ParcelCode")
            .IsUnique();
        builder.HasIndex(entity => new { entity.Status, entity.PickupDueAt })
            .HasDatabaseName("IX_Parcel_Status_PickupDueAt");
        builder.HasIndex(entity => new { entity.Status, entity.MaxStorageUntil })
            .HasDatabaseName("IX_Parcel_Status_MaxStorageUntil");

        builder.ToTable("Parcel", table =>
        {
            table.HasCheckConstraint(
                "CK_Parcel_DeadlineOrder",
                "\"PickupDueAt\" >= \"StoredAt\" AND \"MaxStorageUntil\" >= \"PickupDueAt\"");
            table.HasCheckConstraint(
                "CK_Parcel_TerminalTimestamps",
                "NOT (\"RetrievedAt\" IS NOT NULL AND \"RemovedAt\" IS NOT NULL)");
        });
    }
}
