using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class ResidentProfileConfiguration() : BaseConfiguration<ResidentProfile>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<ResidentProfile> builder)
    {
        Varchar(builder.Property(entity => entity.FullName)).IsRequired();
        Varchar(builder.Property(entity => entity.AvatarUrl));
        EnumAsString(builder.Property(entity => entity.DeliveryApprovalMode)).IsRequired();
        Varchar(builder.Property(entity => entity.PersonalQrTokenHash)).IsRequired();

        builder.HasOne(entity => entity.User)
            .WithOne(user => user.ResidentProfile)
            .HasForeignKey<ResidentProfile>(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.UserId)
            .HasDatabaseName("UX_ResidentProfile_UserId")
            .IsUnique();
        builder.HasIndex(entity => entity.PersonalQrTokenHash)
            .HasDatabaseName("UX_ResidentProfile_PersonalQrTokenHash")
            .IsUnique();
        builder.HasIndex(entity => entity.DeliveryApprovalMode)
            .HasDatabaseName("IX_ResidentProfile_DeliveryApprovalMode");
    }
}
