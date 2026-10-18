using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Services;

public sealed class SubscriptionExpirationWorker(IServiceScopeFactory scopeFactory, ILogger<SubscriptionExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IResidentSubscriptionService>().ExpireOutdatedSubscriptionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Failed to expire resident subscriptions."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
