using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public abstract class BaseConfiguration<TEntity>(Expression<Func<TEntity, object?>> keyExpression)
    : IEntityTypeConfiguration<TEntity>
    where TEntity : class
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.ToTable(typeof(TEntity).Name);
        builder.HasKey(keyExpression);
        ConfigureEntity(builder);
    }

    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);

    protected static PropertyBuilder<TProperty> Varchar<TProperty>(PropertyBuilder<TProperty> property) =>
        property.HasColumnType("character varying");

    protected static PropertyBuilder<TProperty> Text<TProperty>(PropertyBuilder<TProperty> property) =>
        property.HasColumnType("text");

    protected static PropertyBuilder<TEnum> EnumAsString<TEnum>(PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum =>
        property.HasConversion<string>().HasColumnType("character varying");

    protected static PropertyBuilder<TEnum?> NullableEnumAsString<TEnum>(PropertyBuilder<TEnum?> property)
        where TEnum : struct, Enum =>
        property.HasConversion<string>().HasColumnType("character varying");
}
