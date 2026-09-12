using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration() : BaseConfiguration<User>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<User> builder)
    {
        Varchar(builder.Property(entity => entity.PhoneNumber));
        Varchar(builder.Property(entity => entity.Email));
        Varchar(builder.Property(entity => entity.PasswordHash)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Status)).IsRequired();

        builder.HasIndex(entity => entity.PhoneNumber)
            .HasDatabaseName("UX_User_PhoneNumber")
            .HasFilter("\"PhoneNumber\" IS NOT NULL")
            .IsUnique();
        builder.HasIndex(entity => entity.Email)
            .HasDatabaseName("UX_User_Email")
            .HasFilter("\"Email\" IS NOT NULL")
            .IsUnique();
        builder.HasIndex(entity => entity.Status)
            .HasDatabaseName("IX_User_Status");

        builder.ToTable("User", table => table.HasCheckConstraint(
            "CK_User_LoginIdentifier",
            "\"PhoneNumber\" IS NOT NULL OR \"Email\" IS NOT NULL"));
    }
}
