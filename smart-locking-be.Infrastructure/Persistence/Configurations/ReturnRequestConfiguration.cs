using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class ReturnRequestConfiguration() : BaseConfiguration<ReturnRequest>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<ReturnRequest> builder)
    {
        Varchar(builder.Property(entity => entity.ReturnCode)).IsRequired().HasMaxLength(100);
        Text(builder.Property(entity => entity.ReturnReason));
        Varchar(builder.Property(entity => entity.ReturnImageUrl)).HasMaxLength(2048);
        Varchar(builder.Property(entity => entity.ShipperPhone)).HasMaxLength(20);
        Varchar(builder.Property(entity => entity.ShipperSessionTokenHash)).HasMaxLength(256);
        EnumAsString(builder.Property(entity => entity.Status)).IsRequired();
        Text(builder.Property(entity => entity.FailureReason));

        builder.HasOne(entity => entity.ResidentProfile)
            .WithMany(profile => profile.ReturnRequests)
            .HasForeignKey(entity => entity.ResidentProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.OriginalParcel)
            .WithMany(parcel => parcel.ReturnRequests)
            .HasForeignKey(entity => entity.OriginalParcelId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Locker)
            .WithMany(locker => locker.ReturnRequests)
            .HasForeignKey(entity => entity.LockerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.AllocatedCompartment)
            .WithMany(compartment => compartment.ReturnRequests)
            .HasForeignKey(entity => entity.AllocatedCompartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.ReturnCode)
            .HasDatabaseName("UX_ReturnRequest_ReturnCode")
            .IsUnique();
        builder.HasIndex(entity => new { entity.ResidentProfileId, entity.Status })
            .HasDatabaseName("IX_ReturnRequest_Resident_Status");
        builder.HasIndex(entity => new { entity.LockerId, entity.Status })
            .HasDatabaseName("IX_ReturnRequest_Locker_Status");

        builder.ToTable("ReturnRequest", table =>
        {
            table.HasCheckConstraint(
                "CK_ReturnRequest_ReservationExpiresAt",
                "\"ReservationExpiresAt\" IS NULL OR \"AllocatedAt\" IS NULL OR \"ReservationExpiresAt\" > \"AllocatedAt\"");
            table.HasCheckConstraint(
                "CK_ReturnRequest_ResidentDepositedAt",
                "\"ResidentDepositedAt\" IS NULL OR \"AllocatedAt\" IS NULL OR \"ResidentDepositedAt\" >= \"AllocatedAt\"");
            table.HasCheckConstraint(
                "CK_ReturnRequest_ShipperPickedUpAt",
                "\"ShipperPickedUpAt\" IS NULL OR \"ResidentDepositedAt\" IS NULL OR \"ShipperPickedUpAt\" >= \"ResidentDepositedAt\"");
            table.HasCheckConstraint(
                "CK_ReturnRequest_CompartmentReleasedAt",
                "\"CompartmentReleasedAt\" IS NULL OR \"AllocatedAt\" IS NULL OR \"CompartmentReleasedAt\" >= \"AllocatedAt\"");
        });
    }
}
