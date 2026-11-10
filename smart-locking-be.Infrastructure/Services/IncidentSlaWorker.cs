using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smart_locking_be.Infrastructure.Options;

namespace smart_locking_be.Infrastructure.Services;

public sealed class IncidentSlaWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IncidentSlaOptions> options,
    ILogger<IncidentSlaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(Math.Max(30, options.Value.CheckIntervalSeconds)));
        do
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IncidentSlaRunResult result = await scope.ServiceProvider
                    .GetRequiredService<IncidentSlaService>().RunAsync(stoppingToken);
                if (result.Warned > 0 || result.Escalated > 0)
                {
                    logger.LogInformation("Incident SLA run: {Warned} warned, {Escalated} escalated.", result.Warned, result.Escalated);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Incident SLA monitor failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
