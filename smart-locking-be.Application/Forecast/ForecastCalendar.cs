namespace smart_locking_be.Application.Forecast;

public static class ForecastCalendar
{
    private static readonly HashSet<(int Month, int Day)> DemandEventDates =
    [
        (1, 1), (2, 2), (3, 3), (4, 4), (5, 5), (6, 6),
        (7, 7), (8, 8), (9, 9), (10, 10), (11, 11), (12, 12)
    ];

    public static bool IsDemandEventWindow(DateOnly date)
    {
        for (int offset = -3; offset <= 0; offset++)
        {
            DateOnly candidate = date.AddDays(offset);
            if (DemandEventDates.Contains((candidate.Month, candidate.Day)))
                return true;
        }

        return false;
    }

    public static double GetDemandMultiplier(DateOnly date) =>
        DemandEventDates.Contains((date.Month, date.Day))
            ? 1.15
            : IsDemandEventWindow(date)
                ? 1.35
                : 1.0;
}
