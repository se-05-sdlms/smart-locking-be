using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.Infrastructure.Services;

public sealed class OperatorCoverageWorker(IServiceScopeFactory scopes, ILogger<OperatorCoverageWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IOperatorCoverageService>().ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Operator coverage reconciliation failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
