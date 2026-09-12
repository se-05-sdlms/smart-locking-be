using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class LockerEventConfiguration() : BaseConfiguration<LockerEvent>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<LockerEvent> builder)
    {
        EnumAsString(builder.Property(entity => entity.EventType)).IsRequired();
        Varchar(builder.Property(entity => entity.PreviousValue));
        Varchar(builder.Property(entity => entity.NewValue));
        EnumAsString(builder.Property(entity => entity.Severity)).IsRequired();
        Text(builder.Property(entity => entity.Reason));
        Text(builder.Property(entity => entity.Details));

        builder.HasOne(entity => entity.Locker)
            .WithMany(locker => locker.Events)
            .HasForeignKey(entity => entity.LockerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.LockerCompartment)
            .WithMany(compartment => compartment.Events)
            .HasForeignKey(entity => entity.LockerCompartmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.ActorUser)
            .WithMany(user => user.LockerEvents)
            .HasForeignKey(entity => entity.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.LockerId, entity.OccurredAt })
            .HasDatabaseName("IX_LockerEvent_LockerId_OccurredAt");
        builder.HasIndex(entity => new { entity.LockerCompartmentId, entity.OccurredAt })
            .HasDatabaseName("IX_LockerEvent_CompartmentId_OccurredAt");
        builder.HasIndex(entity => new { entity.EventType, entity.OccurredAt })
            .HasDatabaseName("IX_LockerEvent_EventType_OccurredAt");
    }
}
