using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class OperatorSupportContactConfiguration() : BaseConfiguration<OperatorSupportContact>(x => x.OperatorUserId)
{
    protected override void ConfigureEntity(EntityTypeBuilder<OperatorSupportContact> b)
    {
        Varchar(b.Property(x => x.DisplayName)).HasMaxLength(200);
        Varchar(b.Property(x => x.WorkPhoneNumber)).HasMaxLength(16);
        Varchar(b.Property(x => x.Instructions)).HasMaxLength(1000);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OperatorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
