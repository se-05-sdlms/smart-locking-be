namespace smart_locking_be.Domain.Enums;

public enum IncidentSource
{
    Resident,
    Shipper,
    System
}

public enum IncidentStatus
{
    Open,
    Investigating,
    Resolved,
    Escalated
}

public enum LockerEventType
{
    ConnectionChanged,
    DoorChanged,
    OperationalStatusChanged,
    DeviceAlert
}

public enum LockerEventSeverity
{
    Info,
    Warning,
    Critical
}

public enum EmergencyUnlockResult
{
    Pending,
    Succeeded,
    Failed
}

public enum MaintenancePriority
{
    Low,
    Normal,
    High,
    Critical
}

public enum MaintenanceStatus
{
    Open,
    InProgress,
    Closed,
    Cancelled
}
