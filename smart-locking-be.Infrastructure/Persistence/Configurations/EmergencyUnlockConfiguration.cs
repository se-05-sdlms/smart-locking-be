using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class EmergencyUnlockConfiguration() : BaseConfiguration<EmergencyUnlock>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<EmergencyUnlock> builder)
    {
        Text(builder.Property(entity => entity.Reason)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Result)).IsRequired();

        builder.HasOne(entity => entity.OperatorUser)
            .WithMany(user => user.EmergencyUnlocks)
            .HasForeignKey(entity => entity.OperatorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Locker)
            .WithMany(locker => locker.EmergencyUnlocks)
            .HasForeignKey(entity => entity.LockerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.LockerCompartment)
            .WithMany(compartment => compartment.EmergencyUnlocks)
            .HasForeignKey(entity => entity.LockerCompartmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Incident)
            .WithMany(incident => incident.EmergencyUnlocks)
            .HasForeignKey(entity => entity.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.OperatorUserId, entity.RequestedAt })
            .HasDatabaseName("IX_EmergencyUnlock_Operator_RequestedAt");
        builder.HasIndex(entity => new { entity.LockerId, entity.RequestedAt })
            .HasDatabaseName("IX_EmergencyUnlock_Locker_RequestedAt");
        builder.HasIndex(entity => entity.IncidentId)
            .HasDatabaseName("IX_EmergencyUnlock_IncidentId");
    }
}
