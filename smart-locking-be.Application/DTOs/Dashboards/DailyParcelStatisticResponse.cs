namespace smart_locking_be.Application.DTOs.Dashboards;

public sealed record DailyParcelStatisticResponse(
    DateOnly Date,
    string DayLabel,
    int Count
);
