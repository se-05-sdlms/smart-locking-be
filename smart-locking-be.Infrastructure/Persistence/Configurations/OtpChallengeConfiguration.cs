using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class OtpChallengeConfiguration() : BaseConfiguration<OtpChallenge>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<OtpChallenge> builder)
    {
        Varchar(builder.Property(entity => entity.DestinationPhone)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Purpose)).IsRequired();
        Varchar(builder.Property(entity => entity.CodeHash)).IsRequired();

        builder.HasOne(entity => entity.User)
            .WithMany(user => user.OtpChallenges)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Parcel)
            .WithMany(parcel => parcel.OtpChallenges)
            .HasForeignKey(entity => entity.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.DestinationPhone, entity.Purpose, entity.CreatedAt })
            .HasDatabaseName("IX_OtpChallenge_Destination_Purpose_CreatedAt");
        builder.HasIndex(entity => entity.ParcelId)
            .HasDatabaseName("IX_OtpChallenge_ParcelId");

        builder.ToTable("OtpChallenge", table => table.HasCheckConstraint(
            "CK_OtpChallenge_AttemptCount",
            "\"AttemptCount\" >= 0"));
    }
}
