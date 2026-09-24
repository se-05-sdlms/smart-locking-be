namespace smart_locking_be.Application.DTOs.Dashboards;

public sealed record CompartmentStatusDistributionResponse(
    int Total,
    int Available,
    int Occupied,
    int Overdue,
    int Maintenance,
    int Disabled
);
