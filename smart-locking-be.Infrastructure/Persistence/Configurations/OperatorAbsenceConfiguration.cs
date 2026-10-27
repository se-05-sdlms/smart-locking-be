using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class OperatorAbsenceConfiguration() : BaseConfiguration<OperatorAbsence>(x => x.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<OperatorAbsence> builder)
    {
        Varchar(builder.Property(x => x.Reason)).HasMaxLength(1000).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OperatorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OperatorUserId, x.StartsAt, x.EndsAt });
        builder.ToTable(t => t.HasCheckConstraint("CK_OperatorAbsence_Interval", "\"StartsAt\" < \"EndsAt\""));
    }
}
