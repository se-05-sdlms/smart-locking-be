using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Services;

public sealed class DeliveryRequestExpirationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DeliveryRequestExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IDeliveryRequestService service = scope.ServiceProvider.GetRequiredService<IDeliveryRequestService>();
                int expiredCount = await service.ExpireStartedSessionsAsync(stoppingToken);

                if (expiredCount > 0)
                {
                    logger.LogInformation("Expired {DeliveryRequestCount} inactive delivery requests.", expiredCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to expire inactive delivery requests.");
            }
        }
    }
}
