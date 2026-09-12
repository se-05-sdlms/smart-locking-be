using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public UserStatus Status { get; set; }
    public DateTimeOffset? PhoneVerifiedAt { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ResidentProfile? ResidentProfile { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<UserRole> AssignedUserRoles { get; set; } = new List<UserRole>();
    public ICollection<OperatorAssignment> OperatorAssignments { get; set; } = new List<OperatorAssignment>();
    public ICollection<OperatorAssignment> CreatedOperatorAssignments { get; set; } = new List<OperatorAssignment>();
    public ICollection<SystemPolicy> CreatedSystemPolicies { get; set; } = new List<SystemPolicy>();
    public ICollection<OtpChallenge> OtpChallenges { get; set; } = new List<OtpChallenge>();
    public ICollection<Parcel> RemovedParcels { get; set; } = new List<Parcel>();
    public ICollection<ParcelStatusHistory> ParcelStatusChanges { get; set; } = new List<ParcelStatusHistory>();
    public ICollection<ParcelAccessEvent> ParcelAccessEvents { get; set; } = new List<ParcelAccessEvent>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<Incident> ReportedIncidents { get; set; } = new List<Incident>();
    public ICollection<Incident> AssignedIncidents { get; set; } = new List<Incident>();
    public ICollection<IncidentAction> IncidentActions { get; set; } = new List<IncidentAction>();
    public ICollection<LockerEvent> LockerEvents { get; set; } = new List<LockerEvent>();
    public ICollection<EmergencyUnlock> EmergencyUnlocks { get; set; } = new List<EmergencyUnlock>();
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
    public ICollection<MaintenanceActivity> MaintenanceActivities { get; set; } = new List<MaintenanceActivity>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
