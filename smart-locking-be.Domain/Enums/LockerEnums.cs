namespace smart_locking_be.Domain.Enums;

public enum BuildingStatus
{
    Active,
    Inactive
}

public enum LockerClusterStatus
{
    Active,
    Inactive
}

public enum LockerOperationalStatus
{
    Operational,
    OutOfService,
    Inactive
}

public enum LockerConnectionStatus
{
    Online,
    Offline,
    Unknown
}

public enum LockerCompartmentOperationalStatus
{
    Operational,
    OutOfService,
    Inactive
}

public enum DoorStatus
{
    Open,
    Closed,
    Unknown
}
