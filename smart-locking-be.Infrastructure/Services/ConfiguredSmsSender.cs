using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using smart_locking_be.Application.Interfaces.Services;
using System.Text;
using System.Text.Json;

namespace smart_locking_be.Infrastructure.Services;

/// <summary>Configurable SMS provider boundary.</summary>
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

        if (mode.Equals("SpeedSms", StringComparison.OrdinalIgnoreCase))
        {
            await SendWithSpeedSmsAsync(destinationPhone, code, messageId, cancellationToken);
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

    private async Task SendWithSpeedSmsAsync(string destinationPhone, string code, Guid messageId,
        CancellationToken cancellationToken)
    {
        string? accessToken = configuration["Sms:SpeedSmsAccessToken"]?.Trim();
        string endpoint = configuration["Sms:SpeedSmsEndpoint"]?.Trim()
            ?? "https://api.speedsms.vn/index.php/sms/send";
        int smsType = configuration.GetValue("Sms:SpeedSmsType", 2);

        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Any(char.IsControl) ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !new[] { 2, 3, 4, 5, 6 }.Contains(smsType))
            throw Unavailable();

        string speedSmsPhone = destinationPhone.StartsWith('0')
            ? $"84{destinationPhone[1..]}"
            : destinationPhone;
        string content = $"Ma OTP Smart Locking: {code}. Hieu luc 10 phut.";
        using HttpRequestMessage request = new(HttpMethod.Post, uri);
        string basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{accessToken}:"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
        request.Headers.Add("Idempotency-Key", messageId.ToString("N"));
        Dictionary<string, object> payload = new()
        {
            ["to"] = new[] { speedSmsPhone },
            ["content"] = content,
            ["sms_type"] = smsType
        };
        string? sender = configuration["Sms:SpeedSmsSender"]?.Trim();
        if (smsType is 3 or 5)
        {
            if (string.IsNullOrWhiteSpace(sender)) throw Unavailable();
            payload["sender"] = sender;
        }
        request.Content = JsonContent.Create(payload);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            bool accepted = root.TryGetProperty("status", out JsonElement status) &&
                string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("code", out JsonElement resultCode) &&
                string.Equals(resultCode.GetString(), "00", StringComparison.OrdinalIgnoreCase);
            if (!accepted) throw Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            throw Unavailable();
        }
    }

    private sealed record RelayAcknowledgement(bool Accepted, string MessageId);
    private static ServiceUnavailableException Unavailable() =>
        new("sms/unavailable", "SMS could not be accepted for delivery. Try again later.");
}
