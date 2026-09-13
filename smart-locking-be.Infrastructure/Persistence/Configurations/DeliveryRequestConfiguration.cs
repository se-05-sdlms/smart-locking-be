using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class DeliveryRequestConfiguration() : BaseConfiguration<DeliveryRequest>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<DeliveryRequest> builder)
    {
        Varchar(builder.Property(entity => entity.GuestSessionTokenHash)).IsRequired().HasMaxLength(256);
        Varchar(builder.Property(entity => entity.ShipperName)).HasMaxLength(150);
        Varchar(builder.Property(entity => entity.ShipperPhone)).HasMaxLength(20);
        Varchar(builder.Property(entity => entity.RecipientPhoneSnapshot)).HasMaxLength(20);
        Varchar(builder.Property(entity => entity.ParcelImageUrl)).HasMaxLength(2048);
        Varchar(builder.Property(entity => entity.OcrExtractedPhone)).HasMaxLength(20);
        NullableEnumAsString(builder.Property(entity => entity.OcrStatus));
        NullableEnumAsString(builder.Property(entity => entity.ApprovalModeSnapshot));
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
        builder.HasIndex(entity => new { entity.SessionExpiresAt, entity.Status })
            .HasDatabaseName("IX_DeliveryRequest_SessionExpiresAt_Status");
        builder.HasIndex(entity => new { entity.LockerClusterId, entity.Status })
            .HasDatabaseName("IX_DeliveryRequest_Cluster_Status");

        builder.ToTable("DeliveryRequest", table => table.HasCheckConstraint(
            "CK_DeliveryRequest_AllocatedStatusRequiresCompartment",
            "\"Status\" NOT IN ('Allocated', 'Deposited') OR \"AllocatedCompartmentId\" IS NOT NULL"));
    }
}
