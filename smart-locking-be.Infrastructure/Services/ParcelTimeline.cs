namespace smart_locking_be.Infrastructure.Services;

internal static class ParcelTimeline
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static (DateTimeOffset CountStart, DateTimeOffset PickupDue, DateTimeOffset TransferEligible) GetDeadlines(
        DateTimeOffset storedAt,
        int freeStorageHours,
        int maxStorageHours)
    {
        DateTimeOffset local = storedAt.ToOffset(VietnamOffset);
        DateTimeOffset countStart = new(local.Year, local.Month, local.Day, 0, 0, 0, VietnamOffset);
        countStart = countStart.AddDays(1);
        return (
            countStart.ToUniversalTime(),
            countStart.AddHours(freeStorageHours).ToUniversalTime(),
            countStart.AddHours(maxStorageHours).ToUniversalTime());
    }

    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetLocalDayWindow(DateTimeOffset instant)
    {
        DateTimeOffset local = instant.ToOffset(VietnamOffset);
        DateTimeOffset start = new(local.Year, local.Month, local.Day, 0, 0, 0, VietnamOffset);
        return (start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }
}
