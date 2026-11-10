using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace smart_locking_be.Infrastructure.Services;

public sealed class LockerConnectionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<LockerConnectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int seconds = configuration.GetValue<int?>("LockerMonitoring:OfflineAfterSeconds") ?? 90;
        if (seconds < 10) throw new InvalidOperationException("LockerMonitoring:OfflineAfterSeconds must be at least 10.");

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<LockerConnectionMonitor>()
                    .MarkStaleOfflineAsync(TimeSpan.FromSeconds(seconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to update stale locker connections.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
