namespace smart_locking_be.Application.Interfaces.Services;

public interface IOverdueChargeService
{
    Task<int> CalculateAsync(CancellationToken cancellationToken = default);
}
