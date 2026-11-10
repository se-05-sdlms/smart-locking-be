using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Services;

public sealed class OverdueChargeWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OverdueChargeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(15));

        do
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IOverdueChargeService service = scope.ServiceProvider.GetRequiredService<IOverdueChargeService>();
                int processed = await service.CalculateAsync(stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation("Calculated overdue fees for {Parcels} parcels.", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to calculate overdue parcel fees.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
