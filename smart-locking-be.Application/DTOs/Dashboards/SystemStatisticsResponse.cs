namespace smart_locking_be.Application.DTOs.Dashboards;

public sealed record SystemStatisticsResponse(
    int TotalLockers,
    int ActiveLockers,
    int TotalCompartments,
    int AvailableCompartments,
    int OccupiedCompartments,
    int TotalUsers,
    int ResidentCount,
    int OperatorCount,
    int StoredParcelsCount,
    int NewParcelsTodayCount,
    int OverdueParcelsCount,
    int OfflineLockersCount,
    int OpenIncidentsCount,
    int HighPriorityIncidentsCount,
    decimal TotalOverdueFeeAmountLast30Days
);
