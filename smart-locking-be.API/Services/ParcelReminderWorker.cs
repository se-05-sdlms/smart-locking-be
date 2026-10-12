using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Services;

public sealed class ParcelReminderWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ParcelReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                int sent = await scope.ServiceProvider.GetRequiredService<IParcelReminderService>()
                    .SendDailyRemindersAsync(stoppingToken);
                if (sent > 0) logger.LogInformation("Queued {Count} daily parcel pickup reminders.", sent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to create daily parcel pickup reminders.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
