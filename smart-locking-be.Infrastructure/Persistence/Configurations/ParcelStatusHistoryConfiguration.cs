using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class ParcelStatusHistoryConfiguration() : BaseConfiguration<ParcelStatusHistory>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<ParcelStatusHistory> builder)
    {
        NullableEnumAsString(builder.Property(entity => entity.FromStatus));
        EnumAsString(builder.Property(entity => entity.ToStatus)).IsRequired();
        Text(builder.Property(entity => entity.Reason));

        builder.HasOne(entity => entity.Parcel)
            .WithMany(parcel => parcel.StatusHistory)
            .HasForeignKey(entity => entity.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.ChangedByUser)
            .WithMany(user => user.ParcelStatusChanges)
            .HasForeignKey(entity => entity.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.ParcelId, entity.ChangedAt })
            .HasDatabaseName("IX_ParcelStatusHistory_ParcelId_ChangedAt");
        builder.HasIndex(entity => new { entity.ToStatus, entity.ChangedAt })
            .HasDatabaseName("IX_ParcelStatusHistory_ToStatus_ChangedAt");
    }
}
