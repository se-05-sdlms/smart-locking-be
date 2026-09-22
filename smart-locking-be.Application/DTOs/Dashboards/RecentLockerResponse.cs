using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Dashboards;

public sealed record RecentLockerResponse(
    Guid Id,
    string Code,
    string Address,
    string? OperatorName,
    LockerConnectionStatus ConnectionStatus,
    LockerOperationalStatus OperationalStatus,
    DateTimeOffset UpdatedAt
);
