using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration() : BaseConfiguration<RefreshToken>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<RefreshToken> builder)
    {
        Varchar(builder.Property(entity => entity.TokenHash)).IsRequired().HasMaxLength(256);
        Varchar(builder.Property(entity => entity.CreatedByIp)).HasMaxLength(45);
        Varchar(builder.Property(entity => entity.RevokedByIp)).HasMaxLength(45);

        builder.HasOne(entity => entity.User)
            .WithMany(user => user.RefreshTokens)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.ReplacedByToken)
            .WithMany(token => token.ReplacementForTokens)
            .HasForeignKey(entity => entity.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.TokenHash)
            .HasDatabaseName("UX_RefreshToken_TokenHash")
            .IsUnique();
        builder.HasIndex(entity => new { entity.UserId, entity.ExpiresAt })
            .HasDatabaseName("IX_RefreshToken_UserId_ExpiresAt");

        builder.ToTable("RefreshToken", table => table.HasCheckConstraint(
            "CK_RefreshToken_ExpirationAndRevocation",
            "\"ExpiresAt\" > \"CreatedAt\" AND (\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\")"));
    }
}
