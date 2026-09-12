using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class ParcelAccessEventConfiguration() : BaseConfiguration<ParcelAccessEvent>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<ParcelAccessEvent> builder)
    {
        EnumAsString(builder.Property(entity => entity.Method)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Result)).IsRequired();
        Text(builder.Property(entity => entity.FailureReason));
        Varchar(builder.Property(entity => entity.IpAddress));
        Varchar(builder.Property(entity => entity.DeviceContext));

        builder.HasOne(entity => entity.Parcel)
            .WithMany(parcel => parcel.AccessEvents)
            .HasForeignKey(entity => entity.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.User)
            .WithMany(user => user.ParcelAccessEvents)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.ParcelId, entity.OccurredAt })
            .HasDatabaseName("IX_ParcelAccessEvent_ParcelId_OccurredAt");
        builder.HasIndex(entity => new { entity.Result, entity.OccurredAt })
            .HasDatabaseName("IX_ParcelAccessEvent_Result_OccurredAt");
    }
}
