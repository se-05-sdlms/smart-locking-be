using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class LockerSupportSettingsConfiguration() : BaseConfiguration<LockerSupportSettings>(x => x.LockerId)
{
    protected override void ConfigureEntity(EntityTypeBuilder<LockerSupportSettings> b)
    {
        Varchar(b.Property(x => x.FallbackName)).HasMaxLength(200);
        Varchar(b.Property(x => x.FallbackPhoneNumber)).HasMaxLength(16);
        Varchar(b.Property(x => x.FallbackInstructions)).HasMaxLength(1000);
        Varchar(b.Property(x => x.FallbackHours)).HasMaxLength(200);
        b.HasOne<Locker>().WithMany().HasForeignKey(x => x.LockerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
