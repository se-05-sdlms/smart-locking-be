namespace smart_locking_be.Application.DTOs.Dashboards;

public sealed record LockerStatusDistributionResponse(
    int Total,
    int Operational,
    int Maintenance,
    int Suspended,
    int Offline
);
