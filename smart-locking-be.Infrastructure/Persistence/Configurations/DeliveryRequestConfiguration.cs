using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class DeliveryRequestConfiguration() : BaseConfiguration<DeliveryRequest>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<DeliveryRequest> builder)
    {
        Varchar(builder.Property(entity => entity.GuestSessionTokenHash)).IsRequired();
        Varchar(builder.Property(entity => entity.ShipperName));
        Varchar(builder.Property(entity => entity.ShipperPhone));
        Varchar(builder.Property(entity => entity.RecipientPhoneSnapshot)).IsRequired();
        Varchar(builder.Property(entity => entity.WaybillImageUrl));
        Varchar(builder.Property(entity => entity.OcrExtractedPhone));
        NullableEnumAsString(builder.Property(entity => entity.OcrStatus));
        Varchar(builder.Property(entity => entity.SizeCategory)).IsRequired();
        Text(builder.Property(entity => entity.ParcelDescription));
        EnumAsString(builder.Property(entity => entity.ApprovalModeSnapshot)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Status)).IsRequired();
        NullableEnumAsString(builder.Property(entity => entity.FailureCode));
        Text(builder.Property(entity => entity.FailureDetail));

        builder.HasOne(entity => entity.ResidentProfile)
            .WithMany(profile => profile.DeliveryRequests)
            .HasForeignKey(entity => entity.ResidentProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.LockerCluster)
            .WithMany(cluster => cluster.DeliveryRequests)
            .HasForeignKey(entity => entity.LockerClusterId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.SystemPolicy)
            .WithMany(policy => policy.DeliveryRequests)
            .HasForeignKey(entity => entity.SystemPolicyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.AllocatedCompartment)
            .WithMany(compartment => compartment.DeliveryRequests)
            .HasForeignKey(entity => entity.AllocatedCompartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.GuestSessionTokenHash)
            .HasDatabaseName("UX_DeliveryRequest_GuestSessionTokenHash")
            .IsUnique();
        builder.HasIndex(entity => new { entity.ResidentProfileId, entity.Status })
            .HasDatabaseName("IX_DeliveryRequest_Resident_Status");
        builder.HasIndex(entity => new { entity.ExpiresAt, entity.Status })
            .HasDatabaseName("IX_DeliveryRequest_ExpiresAt_Status");
        builder.HasIndex(entity => new { entity.LockerClusterId, entity.Status })
            .HasDatabaseName("IX_DeliveryRequest_Cluster_Status");
        builder.HasIndex(entity => entity.AllocatedCompartmentId)
            .HasDatabaseName("UX_DeliveryRequest_ActiveCompartment")
            .HasFilter("\"CompartmentReleasedAt\" IS NULL AND \"AllocatedCompartmentId\" IS NOT NULL")
            .IsUnique();

        builder.ToTable("DeliveryRequest", table => table.HasCheckConstraint(
            "CK_DeliveryRequest_AllocatedStatusRequiresCompartment",
            "\"Status\" NOT IN ('Allocated', 'Deposited') OR \"AllocatedCompartmentId\" IS NOT NULL"));
    }
}
