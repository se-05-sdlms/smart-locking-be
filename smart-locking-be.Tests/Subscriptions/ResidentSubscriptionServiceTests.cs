using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Subscriptions;

public sealed class ResidentSubscriptionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Checkout_RemainsPending_AndPreservesPurchasedPriceAndDuration()
    {
        await using var db = CreateDb();
        var (resident, plan) = await Seed(db);
        var gateway = new Gateway(db);
        var service = CreateService(db, gateway);
        var checkout = await service.SubscribeAsync(resident.UserId, new(plan.Id));
        Assert.Equal("https://pay.example/checkout", checkout.CheckoutUrl);
        Assert.True(checkout.OrderCode > 0);
        var row = await db.ResidentSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.PendingPayment, row.Status);
        Assert.Null(row.PaidAt);
        Assert.Null(await service.GetCurrentAsync(resident.UserId));
        Assert.Equal(50000, gateway.LastRequest!.Amount);
        plan.DurationDays = 60;
        plan.Price = 90000;
        await db.SaveChangesAsync();
        await service.ActivateSubscriptionAsync(new(checkout.OrderCode, 50000, "tx1", "00", "VND"));
        Assert.Equal(Now, row.StartDate);
        Assert.Equal(Now.AddDays(30), row.EndDate);
        Assert.Equal(50000, row.PriceSnapshot);
    }

    [Fact]
    public async Task Activate_StacksAfterLatestEnd_AndDuplicatesNeverExtendEvenAfterExpiry()
    {
        await using var db = CreateDb();
        var (resident, plan) = await Seed(db);
        var service = CreateService(db, new Gateway(db));
        var first = await service.SubscribeAsync(resident.UserId, new(plan.Id));
        var second = await service.SubscribeAsync(resident.UserId, new(plan.Id));
        Assert.NotEqual(first.OrderCode, second.OrderCode);
        await service.ActivateSubscriptionAsync(new(first.OrderCode, 50000, "first", "00", "VND"));
        await service.ActivateSubscriptionAsync(new(second.OrderCode, 50000, "second", "00", "VND"));
        await service.ActivateSubscriptionAsync(new(second.OrderCode, 50000, "second", "00", "VND"));
        var stacked = await db.ResidentSubscriptions.SingleAsync(s => s.Id == second.SubscriptionId);
        Assert.Equal(Now.AddDays(30), stacked.StartDate);
        Assert.Equal(Now.AddDays(60), stacked.EndDate);
        Assert.Equal(first.SubscriptionId, (await service.GetCurrentAsync(resident.UserId))!.Id);
        stacked.Status = SubscriptionStatus.Expired;
        await db.SaveChangesAsync();
        await service.ActivateSubscriptionAsync(new(second.OrderCode, 50000, "second", "00", "VND"));
        Assert.Equal(SubscriptionStatus.Expired, stacked.Status);
        Assert.Equal(Now.AddDays(60), stacked.EndDate);
    }

    [Theory]
    [InlineData(49999, "00", "VND")]
    [InlineData(50000, "01", "VND")]
    [InlineData(50000, "00", "USD")]
    public async Task Activate_InvalidPayment_DoesNotActivate(int amount, string code, string currency)
    {
        await using var db = CreateDb();
        var (resident, plan) = await Seed(db);
        var service = CreateService(db, new Gateway(db));
        var checkout = await service.SubscribeAsync(resident.UserId, new(plan.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ActivateSubscriptionAsync(new(checkout.OrderCode, amount, "tx", code, currency)));
        Assert.Equal(SubscriptionStatus.PendingPayment, (await db.ResidentSubscriptions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Expire_OnlyPastActivePeriods_HistoryStaysResidentScoped()
    {
        await using var db = CreateDb();
        var (resident, plan) = await Seed(db);
        var service = CreateService(db, new Gateway(db));
        var checkout = await service.SubscribeAsync(resident.UserId, new(plan.Id));
        await service.ActivateSubscriptionAsync(new(checkout.OrderCode, 50000, "tx", "00", "VND"));
        var row = await db.ResidentSubscriptions.SingleAsync();
        row.EndDate = Now.AddSeconds(-1);
        await db.SaveChangesAsync();
        Assert.Equal(1, await service.ExpireOutdatedSubscriptionsAsync());
        Assert.Equal(SubscriptionStatus.Expired, row.Status);
        Assert.Equal(0, await service.ExpireOutdatedSubscriptionsAsync());
        Assert.Empty(await service.GetHistoryAsync(Guid.NewGuid()));
        Assert.Single(await service.GetHistoryAsync(resident.UserId));
    }

    [Fact]
    public async Task Checkout_GatewayFailure_LeavesRecoverablePendingOrder()
    {
        await using var db = CreateDb();
        var (resident, plan) = await Seed(db);
        var service = CreateService(db, new Gateway(db) { Fail = true });
        await Assert.ThrowsAsync<HttpRequestException>(() => service.SubscribeAsync(resident.UserId, new(plan.Id)));
        var row = await db.ResidentSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.PendingPayment, row.Status);
        Assert.NotNull(row.ExternalOrderCode);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(ResidentProfile, SubscriptionPlan)> Seed(ApplicationDbContext db)
    {
        var resident = new ResidentProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), FullName = "Resident" };
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Code = "BASIC", Name = "Basic", IsActive = true, Price = 50000, DurationDays = 30, MaxActiveParcels = 2 };
        db.AddRange(resident, plan);
        await db.SaveChangesAsync();
        return (resident, plan);
    }

    private static ResidentSubscriptionService CreateService(ApplicationDbContext db, Gateway gateway) => new(db, gateway,
        Options.Create(new PayOSOptions { ReturnUrl = "https://app.example/success", CancelUrl = "https://app.example/cancel" }), new Clock());

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Gateway(ApplicationDbContext db) : IPaymentGatewayService
    {
        public bool Fail { get; init; }
        public CreatePaymentLinkRequest? LastRequest { get; private set; }
        public async Task<CreatePaymentLinkResult> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken = default)
        {
            Assert.True(await db.ResidentSubscriptions.AnyAsync(s => s.ExternalOrderCode == request.OrderCode.ToString(), cancellationToken));
            if (Fail) throw new HttpRequestException("Unavailable");
            LastRequest = request;
            return new("https://pay.example/checkout", "link", "qr");
        }
        public PayOSWebhookData VerifyWebhook(PayOSWebhookPayload payload) => throw new NotSupportedException();
    }
}
