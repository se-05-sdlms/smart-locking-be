using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Payments;

public sealed class PayOSPaymentGatewayServiceTests
{
    private const string ChecksumKey = "test-checksum-key";
    private const string DataJson = """
        {"orderCode":123456,"amount":50000,"description":"Subscription","accountNumber":"123456789",
         "reference":"TXN-123","transactionDateTime":"2026-10-13 10:30:00","currency":"VND",
         "paymentLinkId":"link-123","code":"00","desc":"success","counterAccountBankId":"970422",
         "counterAccountBankName":"MB","counterAccountName":"Test Buyer","counterAccountNumber":"987654321",
         "virtualAccountName":"SMART LOCK","virtualAccountNumber":"VA123"}
        """;

    [Fact]
    public void VerifyWebhook_ValidSignature_PreservesAllSignedFieldsAndMapsVerifiedData()
    {
        var result = CreateService().VerifyWebhook(Payload());
        Assert.Equal(new PayOSWebhookData(123456, 50000, "TXN-123", "00", "VND"), result);
    }

    [Fact]
    public void VerifyWebhook_TamperedOptionalField_RejectsSignature()
    {
        var payload = Payload();
        payload = payload with { Data = JsonSerializer.Deserialize<JsonElement>(DataJson.Replace("Test Buyer", "Attacker")) };
        Assert.Throws<ArgumentException>(() => CreateService().VerifyWebhook(payload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-signature")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public void VerifyWebhook_InvalidSignature_IsRejected(string signature) =>
        Assert.Throws<ArgumentException>(() => CreateService().VerifyWebhook(Payload() with { Signature = signature }));

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"orderCode\":\"wrong\"}")]
    public void VerifyWebhook_MalformedData_IsRejected(string json) =>
        Assert.Throws<ArgumentException>(() => CreateService().VerifyWebhook(Payload() with { Data = JsonSerializer.Deserialize<JsonElement>(json) }));

    [Theory]
    [InlineData("ClientId")]
    [InlineData("ApiKey")]
    [InlineData("ChecksumKey")]
    public async Task MissingCredentials_FailClosedForBothOperations(string missing)
    {
        var settings = Settings();
        typeof(PayOSOptions).GetProperty(missing)!.SetValue(settings, " ");
        var service = new PayOSPaymentGatewayService(Options.Create(settings));
        Assert.Throws<InvalidOperationException>(() => service.VerifyWebhook(Payload()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePaymentLinkAsync(Request()));
    }

    [Fact]
    public async Task CreatePaymentLink_PreCancelled_DoesNotCallProvider()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService().CreatePaymentLinkAsync(Request(), cancellation.Token));
    }

    [Fact]
    public async Task CreatePaymentLink_InvalidAmount_IsRejectedBeforeNetwork() =>
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().CreatePaymentLinkAsync(Request() with { Amount = 0 }));

    private static PayOSPaymentGatewayService CreateService() => new(Options.Create(Settings()));
    private static PayOSOptions Settings() => new() { ClientId = "test-client", ApiKey = "test-api", ChecksumKey = ChecksumKey };
    private static CreatePaymentLinkRequest Request() => new(123456, 50000, "Subscription", "https://example.com/success", "https://example.com/cancel");

    private static PayOSWebhookPayload Payload()
    {
        // Independent provider-format fixture: all data keys in ordinal alphabetical order.
        const string canonical = "accountNumber=123456789&amount=50000&code=00&counterAccountBankId=970422&counterAccountBankName=MB&counterAccountName=Test Buyer&counterAccountNumber=987654321&currency=VND&desc=success&description=Subscription&orderCode=123456&paymentLinkId=link-123&reference=TXN-123&transactionDateTime=2026-10-13 10:30:00&virtualAccountName=SMART LOCK&virtualAccountNumber=VA123";
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ChecksumKey), Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new("00", "success", true, JsonSerializer.Deserialize<JsonElement>(DataJson), signature);
    }
}
