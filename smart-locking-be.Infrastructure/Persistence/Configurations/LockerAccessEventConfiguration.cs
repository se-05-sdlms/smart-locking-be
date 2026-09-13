using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class LockerAccessEventConfiguration() : BaseConfiguration<LockerAccessEvent>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<LockerAccessEvent> builder)
    {
        EnumAsString(builder.Property(entity => entity.AccessType)).IsRequired();
        EnumAsString(builder.Property(entity => entity.AccessMethod)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Result)).IsRequired();
        Text(builder.Property(entity => entity.FailureReason));
        Varchar(builder.Property(entity => entity.IpAddress)).HasMaxLength(45);
        Text(builder.Property(entity => entity.DeviceContext));

        builder.HasOne(entity => entity.Locker)
            .WithMany(locker => locker.AccessEvents)
            .HasForeignKey(entity => entity.LockerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.LockerCompartment)
            .WithMany(compartment => compartment.AccessEvents)
            .HasForeignKey(entity => entity.LockerCompartmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.User)
            .WithMany(user => user.LockerAccessEvents)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.DeliveryRequest)
            .WithMany(request => request.AccessEvents)
            .HasForeignKey(entity => entity.DeliveryRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Parcel)
            .WithMany(parcel => parcel.AccessEvents)
            .HasForeignKey(entity => entity.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.ReturnRequest)
            .WithMany(request => request.AccessEvents)
            .HasForeignKey(entity => entity.ReturnRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.LockerId, entity.OccurredAt })
            .HasDatabaseName("IX_LockerAccessEvent_Locker_OccurredAt");
        builder.HasIndex(entity => new { entity.LockerCompartmentId, entity.OccurredAt })
            .HasDatabaseName("IX_LockerAccessEvent_Compartment_OccurredAt");
        builder.HasIndex(entity => new { entity.UserId, entity.OccurredAt })
            .HasDatabaseName("IX_LockerAccessEvent_User_OccurredAt");
        builder.HasIndex(entity => entity.DeliveryRequestId)
            .HasDatabaseName("IX_LockerAccessEvent_DeliveryRequestId");
        builder.HasIndex(entity => entity.ParcelId)
            .HasDatabaseName("IX_LockerAccessEvent_ParcelId");
        builder.HasIndex(entity => entity.ReturnRequestId)
            .HasDatabaseName("IX_LockerAccessEvent_ReturnRequestId");
    }
}
