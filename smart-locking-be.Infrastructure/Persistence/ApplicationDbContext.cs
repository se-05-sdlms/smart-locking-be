using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ResidentProfile> ResidentProfiles => Set<ResidentProfile>();
    public DbSet<ResidentBiometric> ResidentBiometrics => Set<ResidentBiometric>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Locker> Lockers => Set<Locker>();
    public DbSet<LockerCompartment> LockerCompartments => Set<LockerCompartment>();
    public DbSet<OperatorAssignment> OperatorAssignments => Set<OperatorAssignment>();
    public DbSet<SystemPolicy> SystemPolicies => Set<SystemPolicy>();
    public DbSet<NotificationRule> NotificationRules => Set<NotificationRule>();
    public DbSet<DeliveryRequest> DeliveryRequests => Set<DeliveryRequest>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    public DbSet<CompartmentReservation> CompartmentReservations => Set<CompartmentReservation>();
    public DbSet<Parcel> Parcels => Set<Parcel>();
    public DbSet<ParcelStatusHistory> ParcelStatusHistories => Set<ParcelStatusHistory>();
    public DbSet<LockerAccessEvent> LockerAccessEvents => Set<LockerAccessEvent>();
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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAuditLogImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceAuditLogImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceAuditLogImmutability()
    {
        var invalidEntries = ChangeTracker.Entries<AuditLog>()
            .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted);

        if (invalidEntries.Any())
        {
            throw new InvalidOperationException("AuditLog records are append-only and cannot be modified or deleted.");
        }
    }

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
