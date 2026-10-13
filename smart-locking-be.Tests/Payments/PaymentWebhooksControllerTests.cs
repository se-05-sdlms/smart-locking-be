using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using smart_locking_be.API.Controllers;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Payments;

public sealed class PaymentWebhooksControllerTests
{
    [Fact]
    public async Task Webhook_VerifiedUnknownOrder_AcknowledgesProviderProbeWithoutCreatingSubscription()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var gateway = new Gateway(true);
        var subscriptions = new ResidentSubscriptionService(db, gateway, Options.Create(new PayOSOptions()), TimeProvider.System);
        var controller = new PaymentWebhooksController(gateway, subscriptions);
        var result = await controller.PayOS(new("00", "success", true, JsonSerializer.SerializeToElement(new { }), "signature"), default);
        Assert.IsType<OkResult>(result);
        Assert.Empty(db.ResidentSubscriptions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Webhook_OnlyVerifiedPaymentActivates(bool validSignature)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var row = new ResidentSubscription
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = Guid.NewGuid(),
            SubscriptionPlanId = Guid.NewGuid(),
            ExternalOrderCode = "123",
            PriceSnapshot = 50000,
            Status = SubscriptionStatus.PendingPayment,
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddDays(30)
        };
        db.Add(row);
        await db.SaveChangesAsync();
        var gateway = new Gateway(validSignature);
        var subscriptions = new ResidentSubscriptionService(db, gateway, Options.Create(new PayOSOptions()), TimeProvider.System);
        var controller = new PaymentWebhooksController(gateway, subscriptions);
        var result = await controller.PayOS(new("00", "success", true, JsonSerializer.SerializeToElement(new { }), "signature"), default);
        if (validSignature)
        {
            Assert.IsType<OkResult>(result);
            Assert.Equal(SubscriptionStatus.Active, row.Status);
            Assert.Equal("ref123", row.ExternalTransactionId);
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(SubscriptionStatus.PendingPayment, row.Status);
            Assert.Null(row.PaidAt);
        }
    }

    private sealed class Gateway(bool valid) : IPaymentGatewayService
    {
        public Task<CreatePaymentLinkResult> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public PayOSWebhookData VerifyWebhook(PayOSWebhookPayload payload) => valid
            ? new(123, 50000, "ref123", "00", "VND") : throw new ArgumentException("Invalid signature");
    }
}
