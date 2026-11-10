using Microsoft.Extensions.Configuration;
using smart_locking_be.Application.Interfaces.Services;
using System.Net;
using System.Net.Mail;

namespace smart_locking_be.Infrastructure.Services;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        string? host = configuration["Email:Smtp:Host"];
        string? from = configuration["Email:Smtp:From"];
        string? username = configuration["Email:Smtp:Username"];
        string? password = configuration["Email:Smtp:Password"];
        int port = configuration.GetValue<int>("Email:Smtp:Port");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) ||
            string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || port is < 1 or > 65535)
            throw new ServiceUnavailableException(UserErrorCodes.EmailUnavailable, "SMTP email is not configured.");

        try
        {
            using MailMessage message = new(from, to, subject, body);
            using SmtpClient client = new(host, port)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(username, password),
                Timeout = 10000
            };
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new ServiceUnavailableException(UserErrorCodes.EmailUnavailable,
                "Credential email could not be delivered. Try again or reissue the password.", exception);
        }
    }
}
