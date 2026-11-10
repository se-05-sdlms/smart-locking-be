using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class ReturnCancellationRequestConfiguration() : BaseConfiguration<ReturnCancellationRequest>(item => item.ReturnRequestId)
{
    protected override void ConfigureEntity(EntityTypeBuilder<ReturnCancellationRequest> builder)
    {
        Varchar(builder.Property(item => item.Status)).HasMaxLength(40).IsRequired();
        builder.HasOne(item => item.ReturnRequest).WithOne(item => item.Cancellation)
            .HasForeignKey<ReturnCancellationRequest>(item => item.ReturnRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.ResidentUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
