using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<ResidentProfile> ResidentProfiles => Set<ResidentProfile>();
    public DbSet<ResidentBiometric> ResidentBiometrics => Set<ResidentBiometric>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<LockerCluster> LockerClusters => Set<LockerCluster>();
    public DbSet<Locker> Lockers => Set<Locker>();
    public DbSet<LockerCompartment> LockerCompartments => Set<LockerCompartment>();
    public DbSet<OperatorAssignment> OperatorAssignments => Set<OperatorAssignment>();
    public DbSet<SystemPolicy> SystemPolicies => Set<SystemPolicy>();
    public DbSet<NotificationRule> NotificationRules => Set<NotificationRule>();
    public DbSet<DeliveryRequest> DeliveryRequests => Set<DeliveryRequest>();
    public DbSet<Parcel> Parcels => Set<Parcel>();
    public DbSet<ParcelStatusHistory> ParcelStatusHistories => Set<ParcelStatusHistory>();
    public DbSet<ParcelAccessEvent> ParcelAccessEvents => Set<ParcelAccessEvent>();
    public DbSet<OverdueCharge> OverdueCharges => Set<OverdueCharge>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentAction> IncidentActions => Set<IncidentAction>();
    public DbSet<LockerEvent> LockerEvents => Set<LockerEvent>();
    public DbSet<EmergencyUnlock> EmergencyUnlocks => Set<EmergencyUnlock>();
    public DbSet<MaintenanceRequest> MaintenanceRequests => Set<MaintenanceRequest>();
    public DbSet<MaintenanceActivity> MaintenanceActivities => Set<MaintenanceActivity>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
