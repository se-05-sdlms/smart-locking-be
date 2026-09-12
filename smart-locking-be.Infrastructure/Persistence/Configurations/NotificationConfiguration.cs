using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration() : BaseConfiguration<Notification>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<Notification> builder)
    {
        Varchar(builder.Property(entity => entity.Type)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Channel)).IsRequired();
        Varchar(builder.Property(entity => entity.Title)).IsRequired();
        Text(builder.Property(entity => entity.Message)).IsRequired();
        EnumAsString(builder.Property(entity => entity.DeliveryStatus)).IsRequired();

        builder.HasOne(entity => entity.User)
            .WithMany(user => user.Notifications)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Parcel)
            .WithMany(parcel => parcel.Notifications)
            .HasForeignKey(entity => entity.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Incident)
            .WithMany(incident => incident.Notifications)
            .HasForeignKey(entity => entity.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.PaymentTransaction)
            .WithMany(payment => payment.Notifications)
            .HasForeignKey(entity => entity.PaymentTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.UserId, entity.IsRead, entity.CreatedAt })
            .HasDatabaseName("IX_Notification_UserId_IsRead_CreatedAt");
        builder.HasIndex(entity => new { entity.Type, entity.CreatedAt })
            .HasDatabaseName("IX_Notification_Type_CreatedAt");

        builder.ToTable("Notification", table => table.HasCheckConstraint(
            "CK_Notification_ReadState",
            "\"ReadAt\" IS NULL OR \"IsRead\" = TRUE"));
    }
}
