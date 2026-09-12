using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Tests;

public sealed class PersistenceModelTests
{
    [Fact]
    public void Model_contains_every_documented_entity()
    {
        using var context = CreateContext();

        string[] actualEntityNames = context.Model
            .GetEntityTypes()
            .Select(entityType => entityType.ClrType.Name)
            .Order()
            .ToArray();

        Assert.Equal(ModelContract.EntityNames, actualEntityNames);
    }

    [Fact]
    public void Entities_contain_exact_documented_scalar_properties()
    {
        using var context = CreateContext();

        foreach ((string entityName, PropertyExpectation[] expectedProperties) in ModelContract.Properties)
        {
            var entityType = context.Model.GetEntityTypes()
                .Single(entity => entity.ClrType.Name == entityName);

            var actualProperties = entityType.GetProperties()
                .Select(property => new PropertyExpectation(
                    property.Name,
                    Nullable.GetUnderlyingType(property.ClrType)?.Name ?? property.ClrType.Name,
                    property.IsNullable))
                .OrderBy(property => property.Name)
                .ToArray();

            Assert.Equal(expectedProperties.OrderBy(property => property.Name), actualProperties);
        }
    }

    [Fact]
    public void Entities_use_documented_tables_primary_keys_and_string_storage()
    {
        using var context = CreateContext();
        var textProperties = new HashSet<string>
        {
            "Role.Description", "Permission.Description", "Building.Address",
            "LockerCluster.LocationDescription", "OperatorAssignment.Reason",
            "DeliveryRequest.ParcelDescription", "DeliveryRequest.FailureDetail", "Parcel.RemovalReason",
            "ParcelStatusHistory.Reason", "ParcelAccessEvent.FailureReason", "PaymentTransaction.FailureReason",
            "Notification.Message", "Incident.Description", "Incident.ResolutionSummary", "IncidentAction.Notes",
            "LockerEvent.Reason", "LockerEvent.Details", "EmergencyUnlock.Reason",
            "MaintenanceRequest.Description", "MaintenanceRequest.ResolutionSummary",
            "MaintenanceActivity.Notes", "AuditLog.Details"
        };

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            Assert.Equal(entityType.ClrType.Name, entityType.GetTableName());
            Assert.Equal(["Id"], entityType.FindPrimaryKey()!.Properties.Select(property => property.Name));

            foreach (var property in entityType.GetProperties().Where(property => property.ClrType == typeof(string) ||
                         property.ClrType.IsEnum || Nullable.GetUnderlyingType(property.ClrType)?.IsEnum == true))
            {
                string qualifiedName = $"{entityType.ClrType.Name}.{property.Name}";
                Assert.Equal(textProperties.Contains(qualifiedName) ? "text" : "character varying", property.GetColumnType());

                Type enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (enumType.IsEnum)
                {
                    Assert.Equal(typeof(string), property.GetTypeMapping().Converter?.ProviderClrType);
                }
            }
        }
    }

    [Fact]
    public void Model_contains_every_documented_index_and_filter()
    {
        using var context = CreateContext();

        foreach (IndexExpectation expected in RelationalModelContract.Indexes)
        {
            var entityType = context.Model.GetEntityTypes().Single(entity => entity.ClrType.Name == expected.EntityName);
            var index = entityType.GetIndexes().Single(candidate => candidate.GetDatabaseName() == expected.DatabaseName);

            Assert.Equal(expected.Properties, index.Properties.Select(property => property.Name));
            Assert.Equal(expected.Unique, index.IsUnique);
            Assert.Equal(expected.Filter, index.GetFilter());
        }
    }

    [Fact]
    public void Model_does_not_add_undocumented_indexes()
    {
        using var context = CreateContext();

        string[] expectedNames = RelationalModelContract.Indexes
            .Select(index => index.DatabaseName)
            .Order()
            .ToArray();
        string[] actualNames = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetIndexes())
            .Select(index => index.GetDatabaseName()!)
            .Order()
            .ToArray();

        Assert.Equal(expectedNames, actualNames);
    }

    [Fact]
    public void Model_contains_every_documented_foreign_key_with_restrict_delete()
    {
        using var context = CreateContext();

        foreach (ForeignKeyExpectation expected in RelationalModelContract.ForeignKeys)
        {
            var entityType = context.Model.GetEntityTypes().Single(entity => entity.ClrType.Name == expected.DependentEntity);
            var foreignKey = entityType.GetForeignKeys().Single(candidate =>
                candidate.PrincipalEntityType.ClrType.Name == expected.PrincipalEntity &&
                candidate.Properties.Select(property => property.Name).SequenceEqual([expected.Property]));

            Assert.Equal(expected.Unique, foreignKey.IsUnique);
            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        }
    }

    [Fact]
    public void Model_contains_database_safe_documented_checks()
    {
        using var context = CreateContext();
        IModel designTimeModel = context.GetService<IDesignTimeModel>().Model;

        foreach ((string entityName, string[] expectedNames) in RelationalModelContract.CheckConstraints)
        {
            var entityType = designTimeModel.GetEntityTypes().Single(entity => entity.ClrType.Name == entityName);
            string[] actualNames = entityType.GetCheckConstraints()
                .Select(constraint => constraint.Name!)
                .Order()
                .ToArray();

            Assert.Equal(expectedNames.Order(), actualNames);
        }
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=sdlms_model_tests;Username=test;Password=test")
            .Options;

        return new ApplicationDbContext(options);
    }
}
