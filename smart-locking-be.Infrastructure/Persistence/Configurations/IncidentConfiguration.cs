using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class IncidentConfiguration() : BaseConfiguration<Incident>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<Incident> builder)
    {
        Varchar(builder.Property(entity => entity.ReporterName));
        Varchar(builder.Property(entity => entity.ReporterPhone));
        Varchar(builder.Property(entity => entity.Type)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Source)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Status)).IsRequired();
        Varchar(builder.Property(entity => entity.Title)).IsRequired();
        Text(builder.Property(entity => entity.Description)).IsRequired();
        Text(builder.Property(entity => entity.ResolutionSummary));

        builder.HasOne(entity => entity.ReporterUser)
            .WithMany(user => user.ReportedIncidents)
            .HasForeignKey(entity => entity.ReporterUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.DeliveryRequest)
            .WithMany(request => request.Incidents)
            .HasForeignKey(entity => entity.DeliveryRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Parcel)
            .WithMany(parcel => parcel.Incidents)
            .HasForeignKey(entity => entity.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Locker)
            .WithMany(locker => locker.Incidents)
            .HasForeignKey(entity => entity.LockerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.LockerCompartment)
            .WithMany(compartment => compartment.Incidents)
            .HasForeignKey(entity => entity.LockerCompartmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.PaymentTransaction)
            .WithMany(payment => payment.Incidents)
            .HasForeignKey(entity => entity.PaymentTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.AssignedOperatorUser)
            .WithMany(user => user.AssignedIncidents)
            .HasForeignKey(entity => entity.AssignedOperatorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.Status, entity.AssignedOperatorUserId })
            .HasDatabaseName("IX_Incident_Status_AssignedOperator");
        builder.HasIndex(entity => entity.ParcelId)
            .HasDatabaseName("IX_Incident_ParcelId");
        builder.HasIndex(entity => new { entity.LockerId, entity.Status })
            .HasDatabaseName("IX_Incident_LockerId_Status");
        builder.HasIndex(entity => entity.CreatedAt)
            .HasDatabaseName("IX_Incident_CreatedAt");

        builder.ToTable("Incident", table => table.HasCheckConstraint(
            "CK_Incident_ResolvedAt",
            "\"Status\" <> 'Resolved' OR \"ResolvedAt\" IS NOT NULL"));
    }
}
