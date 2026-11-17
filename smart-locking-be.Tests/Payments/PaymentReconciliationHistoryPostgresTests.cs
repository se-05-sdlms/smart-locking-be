using Microsoft.EntityFrameworkCore;
using Npgsql;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using smart_locking_be.Tests.Auth;

namespace smart_locking_be.Tests.Payments;

public sealed class PaymentReconciliationHistoryPostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_payment_history_{Guid.NewGuid():N}";
    private readonly Guid adminId = Guid.NewGuid();
    private readonly Guid residentId = Guid.NewGuid();
    private readonly Guid subscriptionId = Guid.NewGuid();
    private readonly Guid caseId = Guid.NewGuid();
    private string connectionString = string.Empty;
    private string adminConnectionString = string.Empty;

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
        var resident = new ResidentProfile { Id = Guid.NewGuid(), UserId = residentId, FullName = "Test resident" };
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Code = "HISTORY-TEST", Name = "Test", Price = 10000, DurationDays = 30 };
        var item = new PaymentReconciliation
        {
            Id = caseId,
            Reference = "REC-HISTORY-TEST",
            ResidentUserId = residentId,
            SubscriptionId = subscriptionId,
            Reason = "Reported debit",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        item.Events.Add(new PaymentReconciliationEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = residentId,
            Action = "Submitted",
            Status = "Submitted",
            Message = "Submitted",
            CreatedAt = item.CreatedAt
        });
        db.AddRange(new User { Id = adminId, Email = "admin@history.test", Role = UserRole.Administrator, Status = UserStatus.Active },
            new User { Id = residentId, Email = "resident@history.test", Role = UserRole.Resident, Status = UserStatus.Active },
            resident, plan, new ResidentSubscription
            {
                Id = subscriptionId,
                ResidentProfileId = resident.Id,
                SubscriptionPlanId = plan.Id,
                Status = SubscriptionStatus.PendingPayment,
                PriceSnapshot = 10000,
                ExternalOrderCode = "123456",
                StartDate = DateTimeOffset.UtcNow,
                EndDate = DateTimeOffset.UtcNow.AddDays(30)
            }, item);
        await db.SaveChangesAsync();
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
    public async Task CreateAndRetry_PersistOneCaseAndSubmittedHistory()
    {
        await using (var seed = Context())
        {
            await seed.Set<PaymentReconciliationEvent>().ExecuteDeleteAsync();
            await seed.Set<PaymentReconciliation>().ExecuteDeleteAsync();
        }
        Guid createdId;
        await using (var db = Context())
        {
            var result = await Service(db).CreateAsync(residentId, new("Subscription", subscriptionId, "Reported debit"));
            createdId = result.Id;
            Assert.Equal("Submitted", result.Status);
            Assert.Single(result.History);
        }
        await using (var db = Context())
            Assert.Equal(createdId, (await Service(db).CreateAsync(residentId, new("Subscription", subscriptionId, "Retry"))).Id);
        await using var read = Context();
        Assert.Single(await read.Set<PaymentReconciliation>().ToListAsync());
        Assert.Equal("Submitted", (await read.Set<PaymentReconciliationEvent>().SingleAsync()).Action);
        Assert.Single(await read.AuditLogs.Where(item => item.Action == "PaymentReconciliation.Submitted").ToListAsync());
        Assert.Empty(await read.Notifications.Where(item => item.Type == "PaymentReconciliationUpdated").ToListAsync());
        Assert.Null((await read.ResidentSubscriptions.SingleAsync()).PaidAt);
    }

    [PostgresFact]
    public async Task AssignExistingCase_PersistsHistoryAndAudit_AndRetryDoesNotDuplicate()
    {
        await using (var db = Context())
        {
            var result = await Service(db).AssignAsync(adminId, caseId);
            Assert.Equal("InReview", result.Status);
            Assert.Equal(2, result.History.Count);
        }
        await using (var db = Context()) await Service(db).AssignAsync(adminId, caseId);
        await AssertPersisted("InReview", "Assigned", notifications: 0);
    }

    [PostgresFact]
    public async Task RespondExistingCase_PersistsAllAllowedStatusesWithoutConfirmingPayment()
    {
        foreach (string status in new[] { "InReview", "WaitingProvider", "Closed" })
        {
            await using var db = Context();
            var result = await Service(db).RespondAsync(adminId, caseId, new(status, $"Response {status}"));
            Assert.Equal(status, result.Status);
        }
        await AssertPersisted("Closed", "Responded", notifications: 3, events: 4, actions: 3);
    }

    [PostgresFact]
    public async Task SynchronizePendingProvider_PersistsEvidenceHistoryAndNotificationWithoutSettlingPayment()
    {
        await using (var db = Context())
        {
            var result = await Service(db).SynchronizeAsync(adminId, caseId);
            Assert.Equal("WaitingProvider", result.Status);
            Assert.Equal("PENDING", result.ProviderStatus);
            Assert.NotNull(result.ProviderCheckedAt);
            Assert.Contains("PENDING", result.History.Single(item => item.Action == "ProviderChecked").ProviderEvidence);
        }
        await AssertPersisted("WaitingProvider", "ProviderChecked", notifications: 1);
    }

    private async Task AssertPersisted(string status, string action, int notifications, int events = 2, int actions = 1)
    {
        await using var db = Context();
        var item = await db.Set<PaymentReconciliation>().SingleAsync();
        Assert.Equal(status, item.Status);
        Assert.Equal(adminId, item.AssignedAdminUserId);
        Assert.Equal(events, await db.Set<PaymentReconciliationEvent>().CountAsync());
        Assert.Equal(actions, await db.Set<PaymentReconciliationEvent>().CountAsync(item => item.Action == action));
        Assert.Equal(actions, await db.AuditLogs.CountAsync(item => item.Action == $"PaymentReconciliation.{action}"));
        Assert.Equal(notifications, await db.Notifications.CountAsync(item => item.UserId == residentId && item.Type == "PaymentReconciliationUpdated"));
        var subscription = await db.ResidentSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.PendingPayment, subscription.Status);
        Assert.Null(subscription.PaidAt);
        Assert.Null(subscription.ExternalTransactionId);
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
    private static PaymentReconciliationService Service(ApplicationDbContext db) => new(db, new PendingGateway(), null!, null!, TimeProvider.System);

    private sealed class PendingGateway : IPaymentGatewayService
    {
        public Task<PaymentGatewayInformation> GetPaymentLinkInformationAsync(long orderCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentGatewayInformation(orderCode, 10000, 0, 10000, "PENDING", []));
        public Task<CreatePaymentLinkResult> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetPaymentLinkStatusAsync(long orderCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> CancelPaymentLinkAsync(long orderCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public PayOSWebhookData VerifyWebhook(PayOSWebhookPayload payload) => throw new NotSupportedException();
    }
}
