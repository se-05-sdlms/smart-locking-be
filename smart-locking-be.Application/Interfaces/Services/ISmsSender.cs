namespace smart_locking_be.Application.Interfaces.Services;

public interface ISmsSender
{
    Task SendOtpAsync(string destinationPhone, string code, string purpose, Guid messageId, CancellationToken cancellationToken);
}
