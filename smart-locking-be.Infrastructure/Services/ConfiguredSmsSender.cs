using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.Infrastructure.Services;

/// <summary>Provider boundary; HttpRelay is our relay protocol, not a guessed vendor API.</summary>
public sealed class ConfiguredSmsSender(HttpClient httpClient, IConfiguration configuration, IHostEnvironment environment,
    ILogger<ConfiguredSmsSender> logger) : ISmsSender
{
    public async Task SendOtpAsync(string destinationPhone, string code, string purpose, Guid messageId, CancellationToken cancellationToken)
    {
        string mode = configuration["Sms:Mode"]?.Trim() ?? "Disabled";
        if (mode.Equals("Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Demo"))
                throw Unavailable();
            // Never enabled by Otp:LogCode alone, and never available in Production.
            if (configuration.GetValue<bool>("Otp:LogCode"))
                logger.LogInformation("Sandbox {Purpose} OTP for {PhoneNumber}: {OtpCode}", purpose, destinationPhone, code);
            return;
        }

        string? endpoint = configuration["Sms:RelayEndpoint"]?.Trim();
        string? apiKey = configuration["Sms:RelayApiKey"];
        if (!mode.Equals("HttpRelay", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsControl))
            throw Unavailable();

        using HttpRequestMessage request = new(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", messageId.ToString("N"));
        request.Content = JsonContent.Create(new { messageId, destinationPhone, code, purpose, expiresInSeconds = 600 });
        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            RelayAcknowledgement? acknowledgement = await response.Content.ReadFromJsonAsync<RelayAcknowledgement>(cancellationToken);
            if (acknowledgement?.Accepted != true || string.IsNullOrWhiteSpace(acknowledgement.MessageId)) throw Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException or NotSupportedException)
        {
            // Avoid logging provider bodies, credentials, phone numbers or OTP values.
            throw Unavailable();
        }
    }

    private sealed record RelayAcknowledgement(bool Accepted, string MessageId);
    private static ServiceUnavailableException Unavailable() =>
        new("sms/unavailable", "SMS could not be accepted for delivery. Try again later.");
}
