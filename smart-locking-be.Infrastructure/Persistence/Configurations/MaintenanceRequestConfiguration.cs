using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class MaintenanceRequestConfiguration() : BaseConfiguration<MaintenanceRequest>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<MaintenanceRequest> builder)
    {
        EnumAsString(builder.Property(entity => entity.Priority)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Status)).IsRequired();
        Text(builder.Property(entity => entity.Description)).IsRequired();
        Text(builder.Property(entity => entity.ResolutionSummary));

        builder.HasOne(entity => entity.Locker)
            .WithMany(locker => locker.MaintenanceRequests)
            .HasForeignKey(entity => entity.LockerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.LockerCompartment)
            .WithMany(compartment => compartment.MaintenanceRequests)
            .HasForeignKey(entity => entity.LockerCompartmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.CreatedByUser)
            .WithMany(user => user.MaintenanceRequests)
            .HasForeignKey(entity => entity.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Incident)
            .WithMany(incident => incident.MaintenanceRequests)
            .HasForeignKey(entity => entity.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.Status, entity.Priority })
            .HasDatabaseName("IX_MaintenanceRequest_Status_Priority");
        builder.HasIndex(entity => new { entity.LockerId, entity.Status })
            .HasDatabaseName("IX_MaintenanceRequest_LockerId_Status");
        builder.HasIndex(entity => entity.CreatedAt)
            .HasDatabaseName("IX_MaintenanceRequest_CreatedAt");

        builder.ToTable("MaintenanceRequest", table => table.HasCheckConstraint(
            "CK_MaintenanceRequest_ClosedAt",
            "\"Status\" <> 'Closed' OR \"ClosedAt\" IS NOT NULL"));
    }
}
