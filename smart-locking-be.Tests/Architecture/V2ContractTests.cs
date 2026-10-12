using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Tests.Architecture;

public sealed class V2ContractTests
{
    [Fact]
    public void Pagination_serializes_items_and_metadata_as_an_object()
    {
        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(new PagedResult<int>([1, 2], 30, 2, 2)));
        Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
        Assert.Equal(30, json.RootElement.GetProperty("TotalCount").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("Items").GetArrayLength());
        Assert.Equal(2, json.RootElement.GetProperty("PageNumber").GetInt32());
    }

    [Fact]
    public void V2_snapshot_matches_the_runtime_relational_model()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=test;Password=test").Options);
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!;
        var snapshotModel = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot.Model, designTime: true);
        var model = db.GetService<IDesignTimeModel>().Model;
        var changes = db.GetService<IMigrationsModelDiffer>().GetDifferences(snapshotModel.GetRelationalModel(), model.GetRelationalModel());
        Assert.Empty(changes);
        Assert.Contains("20261005041237_AddReturnWorkflowNotifications", db.Database.GetMigrations());
        Assert.Contains("20261008122047_RefactorV2Operations", db.Database.GetMigrations());
    }
}
