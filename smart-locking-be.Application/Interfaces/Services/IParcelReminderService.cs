namespace smart_locking_be.Application.Interfaces.Services;

public interface IParcelReminderService
{
    Task<int> SendDailyRemindersAsync(CancellationToken cancellationToken = default);
}
