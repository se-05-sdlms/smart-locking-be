using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using smart_locking_be.Tests.Auth;

namespace smart_locking_be.Tests.Policies;

public sealed class SystemPolicyActivationPostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_activation_test_{Guid.NewGuid():N}";
    private readonly Guid adminId = Guid.NewGuid();
    private string connectionString = string.Empty;
    private string adminConnectionString = string.Empty;
    private Guid originalId;
    private Guid replacementId;
    private Guid alternativeId;

    public async Task InitializeAsync()
    {
        string? configured = Environment.GetEnvironmentVariable("SMART_LOCKING_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var builder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        adminConnectionString = builder.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await create.ExecuteNonQueryAsync();
        builder.Database = databaseName;
        connectionString = builder.ConnectionString;
        await using var db = Context();
        await db.Database.MigrateAsync();
        db.Users.Add(new User
        {
            Id = adminId,
            Email = "activation@example.test",
            PasswordHash = "unused",
            Role = UserRole.Administrator,
            Status = UserStatus.Active
        });
        await db.SaveChangesAsync();
        var service = new SystemPolicyService(db, TimeProvider.System);
        var request = new SaveSystemPolicyRequest(DeliveryApprovalMode.Auto, 15, 30, 10, 168, 0m, "VND", 336,
            336, 24, 5, 5, 15, true, true, false, DateTimeOffset.UtcNow);
        SystemPolicy? active = await db.SystemPolicies.SingleOrDefaultAsync(item => item.IsActive);
        if (active is null)
        {
            originalId = (await service.CreateAsync(adminId, request)).Id;
            await service.ActivateAsync(adminId, originalId);
        }
        else originalId = active.Id;
        replacementId = (await service.CreateAsync(adminId, request)).Id;
        alternativeId = (await service.CreateAsync(adminId, request)).Id;
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(adminConnectionString)) return;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task ReplaceActivePolicy_PreservesUniqueSlotAndRecordsActivationAudit()
    {
        await Activate(replacementId);
        await using var db = Context();
        SystemPolicy active = Assert.Single(await db.SystemPolicies.Where(item => item.IsActive).ToListAsync());
        Assert.Equal(replacementId, active.Id);
        SystemPolicy original = await db.SystemPolicies.SingleAsync(item => item.Id == originalId);
        Assert.False(original.IsActive);
        Assert.Equal(active.EffectiveFrom, original.EffectiveTo);
        Assert.Null(active.EffectiveTo);
        AuditLog audit = Assert.Single(await db.AuditLogs.Where(item => item.Action == "SystemPolicy.Activated" && item.EntityId == replacementId).ToListAsync());
        Assert.Equal(adminId, audit.ActorUserId);
        Assert.Equal(AuditLogResult.Succeeded, audit.Result);
        Assert.Equal(active.EffectiveFrom, audit.OccurredAt);
    }

    [PostgresFact]
    public async Task FailureAfterDeactivation_RollsBackOldPolicyAndDoesNotRecordSuccess()
    {
        await using (var failing = Context(new FailSecondSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new SystemPolicyService(failing, TimeProvider.System)
                .ActivateAsync(adminId, replacementId));
        await using (var db = Context())
        {
            Assert.Equal(originalId, Assert.Single(await db.SystemPolicies.Where(item => item.IsActive).ToListAsync()).Id);
            Assert.False((await db.SystemPolicies.SingleAsync(item => item.Id == replacementId)).IsActive);
            Assert.Null((await db.SystemPolicies.SingleAsync(item => item.Id == originalId)).EffectiveTo);
            Assert.Empty(await db.AuditLogs.Where(item => item.Action == "SystemPolicy.Activated" && item.EntityId == replacementId).ToListAsync());
        }
        // The failed transaction must also release the allocation advisory lock.
        await Activate(replacementId).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [PostgresFact]
    public async Task ConcurrentReplacements_SerializeAndKeepExactlyOneActivePolicy()
    {
        await Task.WhenAll(Activate(replacementId), Activate(alternativeId)).WaitAsync(TimeSpan.FromSeconds(20));
        await using var db = Context();
        SystemPolicy active = Assert.Single(await db.SystemPolicies.Where(item => item.IsActive).ToListAsync());
        Assert.Contains(active.Id, new[] { replacementId, alternativeId });
        Assert.False((await db.SystemPolicies.SingleAsync(item => item.Id == originalId)).IsActive);
        Assert.Equal(2, await db.AuditLogs.CountAsync(item => item.Action == "SystemPolicy.Activated" &&
            (item.EntityId == replacementId || item.EntityId == alternativeId)));
    }

    private ApplicationDbContext Context(SaveChangesInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new ApplicationDbContext(options.Options);
    }

    private async Task Activate(Guid id)
    {
        await using var db = Context();
        await new SystemPolicyService(db, TimeProvider.System).ActivateAsync(adminId, id);
    }

    private sealed class FailSecondSave : SaveChangesInterceptor
    {
        private int saves;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++saves == 2) throw new InvalidOperationException("Test failure after active slot was released");
            return ValueTask.FromResult(result);
        }
    }
}
