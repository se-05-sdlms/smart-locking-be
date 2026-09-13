using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class CompartmentReservationConfiguration() : BaseConfiguration<CompartmentReservation>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<CompartmentReservation> builder)
    {
        builder.HasOne(entity => entity.LockerCompartment)
            .WithMany(compartment => compartment.Reservations)
            .HasForeignKey(entity => entity.LockerCompartmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.DeliveryRequest)
            .WithMany(request => request.Reservations)
            .HasForeignKey(entity => entity.DeliveryRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.ReturnRequest)
            .WithMany(request => request.Reservations)
            .HasForeignKey(entity => entity.ReturnRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.LockerCompartmentId)
            .HasDatabaseName("UX_CompartmentReservation_Active")
            .HasFilter("\"ReleasedAt\" IS NULL")
            .IsUnique();
        builder.HasIndex(entity => entity.DeliveryRequestId)
            .HasDatabaseName("IX_CompartmentReservation_DeliveryRequestId");
        builder.HasIndex(entity => entity.ReturnRequestId)
            .HasDatabaseName("IX_CompartmentReservation_ReturnRequestId");

        builder.ToTable("CompartmentReservation", table =>
        {
            table.HasCheckConstraint(
                "CK_CompartmentReservation_ExactlyOneOwner",
                "(\"DeliveryRequestId\" IS NOT NULL AND \"ReturnRequestId\" IS NULL) OR (\"DeliveryRequestId\" IS NULL AND \"ReturnRequestId\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_CompartmentReservation_ExpiresAfterReserved",
                "\"ExpiresAt\" > \"ReservedAt\"");
            table.HasCheckConstraint(
                "CK_CompartmentReservation_ReleasedAfterReserved",
                "\"ReleasedAt\" IS NULL OR \"ReleasedAt\" >= \"ReservedAt\"");
        });
    }
}
