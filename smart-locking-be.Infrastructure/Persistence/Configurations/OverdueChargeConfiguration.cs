using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class OverdueChargeConfiguration() : BaseConfiguration<OverdueCharge>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<OverdueCharge> builder)
    {
        builder.Property(entity => entity.Amount).HasPrecision(18, 2);
        builder.Property(entity => entity.RatePerHourSnapshot).HasPrecision(18, 2);
        Varchar(builder.Property(entity => entity.Currency)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Status)).IsRequired();

        builder.HasOne(entity => entity.Parcel)
            .WithOne(parcel => parcel.OverdueCharge)
            .HasForeignKey<OverdueCharge>(entity => entity.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.ParcelId)
            .HasDatabaseName("UX_OverdueCharge_ParcelId")
            .IsUnique();
        builder.HasIndex(entity => entity.Status)
            .HasDatabaseName("IX_OverdueCharge_Status");

        builder.ToTable("OverdueCharge", table => table.HasCheckConstraint(
            "CK_OverdueCharge_AmountsAndPeriod",
            "\"Amount\" >= 0 AND \"RatePerHourSnapshot\" >= 0 AND \"CalculatedThrough\" >= \"ChargeStartAt\""));
    }
}
