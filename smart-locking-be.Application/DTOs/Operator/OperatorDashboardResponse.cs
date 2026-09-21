using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Operator;

public sealed record LockerStatusSummaryResponse(
    int Total, int Operational, int OutOfService, int Inactive, int Online, int Offline);

public sealed record CompartmentStatusSummaryResponse(
    int Total, int Available, int Occupied, int Overdue, int Reserved, int Unavailable);

public sealed record ParcelSummaryResponse(int InStorage, int Overdue);

public sealed record IncidentSummaryResponse(int Open, int Investigating, int Escalated, int Resolved, int NeedsAttention);

public sealed record OverdueByLockerResponse(Guid LockerId, string LockerCode, int Count);

public sealed record LockerAttentionResponse(
    Guid LockerId, string LockerCode, string Address,
    LockerOperationalStatus OperationalStatus, LockerConnectionStatus ConnectionStatus,
    int TotalCompartments, int OverdueParcels, int OpenIncidents);

public sealed record OperatorActivityResponse(
    Guid Id, Guid? LockerId, string? LockerCode, string Kind, string Description, DateTimeOffset OccurredAt);

public sealed record OperatorDashboardResponse(
    DateTimeOffset GeneratedAt,
    LockerStatusSummaryResponse Lockers,
    CompartmentStatusSummaryResponse Compartments,
    ParcelSummaryResponse Parcels,
    IncidentSummaryResponse Incidents,
    IReadOnlyList<OverdueByLockerResponse> OverdueByLocker,
    IReadOnlyList<LockerAttentionResponse> AttentionLockers,
    IReadOnlyList<OverdueParcelResponse> RecentOverdueParcels,
    IReadOnlyList<OperatorActivityResponse> TodayActivities);
