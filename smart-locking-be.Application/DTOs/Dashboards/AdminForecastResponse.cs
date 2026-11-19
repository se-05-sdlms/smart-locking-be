namespace smart_locking_be.Application.DTOs.Dashboards;

public sealed record AdminForecastResponse(
    DateTimeOffset GeneratedAt,
    int HorizonDays,
    int CurrentOccupiedCompartments,
    int TotalOperationalCompartments,
    int ForecastPeakOccupiedCompartments,
    int ForecastPeakOccupancyPercent,
    IReadOnlyList<DailyForecastResponse> DailyForecast,
    IReadOnlyList<LockerForecastResponse> AtRiskLockers);

public sealed record DailyForecastResponse(
    DateOnly Date,
    int ForecastIncomingParcels,
    int ForecastPickedUpParcels,
    int ForecastOccupiedCompartments,
    int ForecastOccupancyPercent,
    bool IsDemandEventDay);

public sealed record LockerForecastResponse(
    Guid LockerId,
    string LockerCode,
    int CurrentOccupiedCompartments,
    int TotalOperationalCompartments,
    int ForecastOccupiedCompartments,
    int ForecastOccupancyPercent,
    string RiskLevel,
    string Reason);
