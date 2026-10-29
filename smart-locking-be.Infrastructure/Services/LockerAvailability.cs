using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

/// <summary>
/// Combines the administrator's persisted status with independent operational blocks.
/// Ending one maintenance request never overwrites another cause or the manual status.
/// </summary>
internal static class LockerAvailability
{
    public static IQueryable<MaintenanceRequest> BlockingMaintenance(this ApplicationDbContext dbContext) =>
        dbContext.MaintenanceRequests.Where(item =>
            item.Status == MaintenanceStatus.Open || item.Status == MaintenanceStatus.InProgress ||
            (item.Status == MaintenanceStatus.Closed && item.Incident != null && item.Incident.Status != IncidentStatus.Resolved));

    public static IQueryable<Incident> BlockingEquipmentIncidents(this ApplicationDbContext dbContext) =>
        dbContext.Incidents.Where(item => item.Status != IncidentStatus.Resolved &&
            (item.Type.ToLower() == "locker" || item.Type.ToLower() == "compartment"));

    public static IQueryable<Locker> OperationalLockers(this ApplicationDbContext dbContext)
    {
        IQueryable<MaintenanceRequest> maintenance = dbContext.BlockingMaintenance();
        IQueryable<Incident> incidents = dbContext.BlockingEquipmentIncidents();
        return dbContext.Lockers.Where(locker => locker.OperationalStatus == LockerOperationalStatus.Operational &&
            !maintenance.Any(item => item.LockerId == locker.Id && item.LockerCompartmentId == null) &&
            !incidents.Any(item => item.LockerId == locker.Id && item.LockerCompartmentId == null));
    }

    public static IQueryable<LockerCompartment> OperationalCompartments(this ApplicationDbContext dbContext)
    {
        IQueryable<Locker> lockers = dbContext.OperationalLockers();
        IQueryable<MaintenanceRequest> maintenance = dbContext.BlockingMaintenance();
        IQueryable<Incident> incidents = dbContext.BlockingEquipmentIncidents();
        return dbContext.LockerCompartments.Where(compartment =>
            compartment.OperationalStatus == LockerCompartmentOperationalStatus.Operational &&
            lockers.Any(locker => locker.Id == compartment.LockerId) &&
            !maintenance.Any(item => item.LockerCompartmentId == compartment.Id) &&
            !incidents.Any(item => item.LockerCompartmentId == compartment.Id));
    }

    // Both maintenance mutations and allocation lock the same parent before checking availability.
    // PostgreSQL row locks are transaction scoped and do not change door or occupancy data.
    public static async Task LockScopeAsync(ApplicationDbContext dbContext, Guid lockerId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"Locker\" WHERE \"Id\" = {lockerId} FOR UPDATE", cancellationToken);
    }
}
