using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Dashboards;

public sealed record AttentionLockerResponse(
    Guid Id,
    string Code,
    string Address,
    LockerConnectionStatus ConnectionStatus,
    LockerOperationalStatus OperationalStatus
);
