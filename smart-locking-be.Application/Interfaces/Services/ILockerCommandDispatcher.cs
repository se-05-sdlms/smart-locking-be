using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ILockerCommandDispatcher
{
    Task DispatchUnlockAsync(
        LockerUnlockCommand command,
        CancellationToken cancellationToken = default);
}
