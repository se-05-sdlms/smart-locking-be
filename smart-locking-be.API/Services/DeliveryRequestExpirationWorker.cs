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

                int expiredSessionsCount = await service.ExpireStartedSessionsAsync(stoppingToken);
                int expiredApprovalsCount = await service.ExpirePendingApprovalsAsync(stoppingToken);
                int expiredReservationsCount = await service.ExpireReservationsAsync(stoppingToken);

                int totalExpired = expiredSessionsCount + expiredApprovalsCount + expiredReservationsCount;
                if (totalExpired > 0)
                {
                    logger.LogInformation(
                        "Expired delivery requests summary: {Sessions} sessions, {Approvals} approvals, {Reservations} reservations.",
                        expiredSessionsCount,
                        expiredApprovalsCount,
                        expiredReservationsCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to expire inactive delivery requests/approvals/reservations.");
            }
        }
    }
}
