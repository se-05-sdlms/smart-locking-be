namespace smart_locking_be.Tests;

internal static class RelationalModelContract
{
    internal static readonly IndexExpectation[] Indexes =
    [
        I("User", "UX_User_PhoneNumber", true, "\"PhoneNumber\" IS NOT NULL", "PhoneNumber"),
        I("User", "UX_User_Email", true, "\"Email\" IS NOT NULL", "Email"),
        I("User", "IX_User_Status", false, null, "Status"),
        I("Role", "UX_Role_Name", true, null, "Name"),
        I("Role", "IX_Role_IsActive", false, null, "IsActive"),
        I("Permission", "UX_Permission_Code", true, null, "Code"),
        I("UserRole", "UX_UserRole_UserId_RoleId", true, null, "UserId", "RoleId"),
        I("UserRole", "IX_UserRole_RoleId", false, null, "RoleId"),
        I("RolePermission", "UX_RolePermission_RoleId_PermissionId", true, null, "RoleId", "PermissionId"),
        I("RolePermission", "IX_RolePermission_PermissionId", false, null, "PermissionId"),
        I("ResidentProfile", "UX_ResidentProfile_UserId", true, null, "UserId"),
        I("ResidentProfile", "UX_ResidentProfile_PersonalQrTokenHash", true, null, "PersonalQrTokenHash"),
        I("ResidentProfile", "IX_ResidentProfile_DeliveryApprovalMode", false, null, "DeliveryApprovalMode"),
        I("ResidentBiometric", "UX_ResidentBiometric_ActiveResident", true, "\"RevokedAt\" IS NULL", "ResidentProfileId"),
        I("ResidentBiometric", "IX_ResidentBiometric_TemplateReference", false, null, "TemplateReference"),
        I("OtpChallenge", "IX_OtpChallenge_Destination_Purpose_CreatedAt", false, null, "DestinationPhone", "Purpose", "CreatedAt"),
        I("OtpChallenge", "IX_OtpChallenge_ParcelId", false, null, "ParcelId"),
        I("Building", "UX_Building_Code", true, null, "Code"),
        I("Building", "IX_Building_Status", false, null, "Status"),
        I("LockerCluster", "UX_LockerCluster_BuildingId_Code", true, null, "BuildingId", "Code"),
        I("LockerCluster", "IX_LockerCluster_Status", false, null, "Status"),
        I("Locker", "UX_Locker_DeviceIdentifier", true, null, "DeviceIdentifier"),
        I("Locker", "UX_Locker_ClusterId_Code", true, null, "LockerClusterId", "Code"),
        I("Locker", "IX_Locker_ConnectionStatus_LastSeenAt", false, null, "ConnectionStatus", "LastSeenAt"),
        I("Locker", "IX_Locker_OperationalStatus", false, null, "OperationalStatus"),
        I("LockerCompartment", "UX_LockerCompartment_LockerId_Code", true, null, "LockerId", "Code"),
        I("LockerCompartment", "IX_LockerCompartment_Size_Operational", false, null, "SizeCategory", "OperationalStatus"),
        I("LockerCompartment", "IX_LockerCompartment_DoorStatus", false, null, "DoorStatus"),
        I("OperatorAssignment", "IX_OperatorAssignment_Operator_RevokedAt", false, null, "OperatorUserId", "RevokedAt"),
        I("OperatorAssignment", "UX_OperatorAssignment_ActiveBuilding", true, "\"RevokedAt\" IS NULL AND \"BuildingId\" IS NOT NULL", "OperatorUserId", "BuildingId"),
        I("OperatorAssignment", "UX_OperatorAssignment_ActiveCluster", true, "\"RevokedAt\" IS NULL AND \"LockerClusterId\" IS NOT NULL", "OperatorUserId", "LockerClusterId"),
        I("OperatorAssignment", "UX_OperatorAssignment_ActiveLocker", true, "\"RevokedAt\" IS NULL AND \"LockerId\" IS NOT NULL", "OperatorUserId", "LockerId"),
        I("SystemPolicy", "UX_SystemPolicy_Version", true, null, "Version"),
        I("SystemPolicy", "UX_SystemPolicy_OneActive", true, "\"IsActive\" = TRUE", "IsActive"),
        I("SystemPolicy", "IX_SystemPolicy_EffectiveFrom", false, null, "EffectiveFrom"),
        I("NotificationRule", "UX_NotificationRule_Policy_Event_Channel", true, null, "SystemPolicyId", "EventType", "Channel"),
        I("NotificationRule", "IX_NotificationRule_EventType_IsEnabled", false, null, "EventType", "IsEnabled"),
        I("DeliveryRequest", "UX_DeliveryRequest_GuestSessionTokenHash", true, null, "GuestSessionTokenHash"),
        I("DeliveryRequest", "IX_DeliveryRequest_Resident_Status", false, null, "ResidentProfileId", "Status"),
        I("DeliveryRequest", "IX_DeliveryRequest_ExpiresAt_Status", false, null, "ExpiresAt", "Status"),
        I("DeliveryRequest", "IX_DeliveryRequest_Cluster_Status", false, null, "LockerClusterId", "Status"),
        I("DeliveryRequest", "UX_DeliveryRequest_ActiveCompartment", true, "\"CompartmentReleasedAt\" IS NULL AND \"AllocatedCompartmentId\" IS NOT NULL", "AllocatedCompartmentId"),
        I("Parcel", "UX_Parcel_DeliveryRequestId", true, null, "DeliveryRequestId"),
        I("Parcel", "UX_Parcel_ParcelCode", true, null, "ParcelCode"),
        I("Parcel", "IX_Parcel_Status_PickupDueAt", false, null, "Status", "PickupDueAt"),
        I("Parcel", "IX_Parcel_Status_MaxStorageUntil", false, null, "Status", "MaxStorageUntil"),
        I("ParcelStatusHistory", "IX_ParcelStatusHistory_ParcelId_ChangedAt", false, null, "ParcelId", "ChangedAt"),
        I("ParcelStatusHistory", "IX_ParcelStatusHistory_ToStatus_ChangedAt", false, null, "ToStatus", "ChangedAt"),
        I("ParcelAccessEvent", "IX_ParcelAccessEvent_ParcelId_OccurredAt", false, null, "ParcelId", "OccurredAt"),
        I("ParcelAccessEvent", "IX_ParcelAccessEvent_Result_OccurredAt", false, null, "Result", "OccurredAt"),
        I("OverdueCharge", "UX_OverdueCharge_ParcelId", true, null, "ParcelId"),
        I("OverdueCharge", "IX_OverdueCharge_Status", false, null, "Status"),
        I("PaymentTransaction", "UX_PaymentTransaction_Provider_TransactionId", true, "\"ProviderTransactionId\" IS NOT NULL", "Provider", "ProviderTransactionId"),
        I("PaymentTransaction", "IX_PaymentTransaction_Charge_Status", false, null, "OverdueChargeId", "Status"),
        I("PaymentTransaction", "IX_PaymentTransaction_RequestedAt", false, null, "RequestedAt"),
        I("Notification", "IX_Notification_UserId_IsRead_CreatedAt", false, null, "UserId", "IsRead", "CreatedAt"),
        I("Notification", "IX_Notification_Type_CreatedAt", false, null, "Type", "CreatedAt"),
        I("Incident", "IX_Incident_Status_AssignedOperator", false, null, "Status", "AssignedOperatorUserId"),
        I("Incident", "IX_Incident_ParcelId", false, null, "ParcelId"),
        I("Incident", "IX_Incident_LockerId_Status", false, null, "LockerId", "Status"),
        I("Incident", "IX_Incident_CreatedAt", false, null, "CreatedAt"),
        I("IncidentAction", "IX_IncidentAction_IncidentId_CreatedAt", false, null, "IncidentId", "CreatedAt"),
        I("IncidentAction", "IX_IncidentAction_ActionByUserId_CreatedAt", false, null, "ActionByUserId", "CreatedAt"),
        I("LockerEvent", "IX_LockerEvent_LockerId_OccurredAt", false, null, "LockerId", "OccurredAt"),
        I("LockerEvent", "IX_LockerEvent_CompartmentId_OccurredAt", false, null, "LockerCompartmentId", "OccurredAt"),
        I("LockerEvent", "IX_LockerEvent_EventType_OccurredAt", false, null, "EventType", "OccurredAt"),
        I("EmergencyUnlock", "IX_EmergencyUnlock_Operator_RequestedAt", false, null, "OperatorUserId", "RequestedAt"),
        I("EmergencyUnlock", "IX_EmergencyUnlock_Locker_RequestedAt", false, null, "LockerId", "RequestedAt"),
        I("EmergencyUnlock", "IX_EmergencyUnlock_IncidentId", false, null, "IncidentId"),
        I("MaintenanceRequest", "IX_MaintenanceRequest_Status_Priority", false, null, "Status", "Priority"),
        I("MaintenanceRequest", "IX_MaintenanceRequest_LockerId_Status", false, null, "LockerId", "Status"),
        I("MaintenanceRequest", "IX_MaintenanceRequest_CreatedAt", false, null, "CreatedAt"),
        I("MaintenanceActivity", "IX_MaintenanceActivity_Request_CreatedAt", false, null, "MaintenanceRequestId", "CreatedAt"),
        I("MaintenanceActivity", "IX_MaintenanceActivity_User_CreatedAt", false, null, "ActionByUserId", "CreatedAt"),
        I("AuditLog", "IX_AuditLog_OccurredAt", false, null, "OccurredAt"),
        I("AuditLog", "IX_AuditLog_Actor_Action_OccurredAt", false, null, "ActorUserId", "Action", "OccurredAt"),
        I("AuditLog", "IX_AuditLog_EntityType_EntityId", false, null, "EntityType", "EntityId")
    ];

    internal static readonly ForeignKeyExpectation[] ForeignKeys =
    [
        F("UserRole", "User", "UserId"), F("UserRole", "Role", "RoleId"), F("UserRole", "User", "AssignedByUserId"),
        F("RolePermission", "Role", "RoleId"), F("RolePermission", "Permission", "PermissionId"),
        F("ResidentProfile", "User", "UserId", true), F("ResidentBiometric", "ResidentProfile", "ResidentProfileId"),
        F("OtpChallenge", "User", "UserId"), F("OtpChallenge", "Parcel", "ParcelId"),
        F("LockerCluster", "Building", "BuildingId"), F("Locker", "LockerCluster", "LockerClusterId"),
        F("LockerCompartment", "Locker", "LockerId"), F("OperatorAssignment", "User", "OperatorUserId"),
        F("OperatorAssignment", "Building", "BuildingId"), F("OperatorAssignment", "LockerCluster", "LockerClusterId"),
        F("OperatorAssignment", "Locker", "LockerId"), F("OperatorAssignment", "User", "AssignedByUserId"),
        F("SystemPolicy", "User", "CreatedByUserId"), F("NotificationRule", "SystemPolicy", "SystemPolicyId"),
        F("DeliveryRequest", "ResidentProfile", "ResidentProfileId"), F("DeliveryRequest", "LockerCluster", "LockerClusterId"),
        F("DeliveryRequest", "SystemPolicy", "SystemPolicyId"), F("DeliveryRequest", "LockerCompartment", "AllocatedCompartmentId"),
        F("Parcel", "DeliveryRequest", "DeliveryRequestId", true), F("Parcel", "User", "RemovedByUserId"),
        F("ParcelStatusHistory", "Parcel", "ParcelId"), F("ParcelStatusHistory", "User", "ChangedByUserId"),
        F("ParcelAccessEvent", "Parcel", "ParcelId"), F("ParcelAccessEvent", "User", "UserId"),
        F("OverdueCharge", "Parcel", "ParcelId", true), F("PaymentTransaction", "OverdueCharge", "OverdueChargeId"),
        F("Notification", "User", "UserId"), F("Notification", "Parcel", "ParcelId"),
        F("Notification", "Incident", "IncidentId"), F("Notification", "PaymentTransaction", "PaymentTransactionId"),
        F("Incident", "User", "ReporterUserId"), F("Incident", "DeliveryRequest", "DeliveryRequestId"),
        F("Incident", "Parcel", "ParcelId"), F("Incident", "Locker", "LockerId"),
        F("Incident", "LockerCompartment", "LockerCompartmentId"), F("Incident", "PaymentTransaction", "PaymentTransactionId"),
        F("Incident", "User", "AssignedOperatorUserId"), F("IncidentAction", "Incident", "IncidentId"),
        F("IncidentAction", "User", "ActionByUserId"), F("LockerEvent", "Locker", "LockerId"),
        F("LockerEvent", "LockerCompartment", "LockerCompartmentId"), F("LockerEvent", "User", "ActorUserId"),
        F("EmergencyUnlock", "User", "OperatorUserId"), F("EmergencyUnlock", "Locker", "LockerId"),
        F("EmergencyUnlock", "LockerCompartment", "LockerCompartmentId"), F("EmergencyUnlock", "Incident", "IncidentId"),
        F("MaintenanceRequest", "Locker", "LockerId"), F("MaintenanceRequest", "LockerCompartment", "LockerCompartmentId"),
        F("MaintenanceRequest", "User", "CreatedByUserId"), F("MaintenanceRequest", "Incident", "IncidentId"),
        F("MaintenanceActivity", "MaintenanceRequest", "MaintenanceRequestId"),
        F("MaintenanceActivity", "User", "ActionByUserId"), F("AuditLog", "User", "ActorUserId")
    ];

    internal static readonly IReadOnlyDictionary<string, string[]> CheckConstraints =
        new Dictionary<string, string[]>
        {
            ["User"] = ["CK_User_LoginIdentifier"],
            ["OtpChallenge"] = ["CK_OtpChallenge_AttemptCount"],
            ["OperatorAssignment"] = ["CK_OperatorAssignment_ExactlyOneScope"],
            ["SystemPolicy"] = ["CK_SystemPolicy_DurationsAndRates"],
            ["NotificationRule"] = ["CK_NotificationRule_LeadTimeMinutes"],
            ["DeliveryRequest"] = ["CK_DeliveryRequest_AllocatedStatusRequiresCompartment"],
            ["Parcel"] = ["CK_Parcel_DeadlineOrder", "CK_Parcel_TerminalTimestamps"],
            ["OverdueCharge"] = ["CK_OverdueCharge_AmountsAndPeriod"],
            ["PaymentTransaction"] = ["CK_PaymentTransaction_Amount"],
            ["Notification"] = ["CK_Notification_ReadState"],
            ["Incident"] = ["CK_Incident_ResolvedAt"],
            ["MaintenanceRequest"] = ["CK_MaintenanceRequest_ClosedAt"]
        };

    private static IndexExpectation I(
        string entityName,
        string databaseName,
        bool unique,
        string? filter,
        params string[] properties) => new(entityName, databaseName, unique, filter, properties);

    private static ForeignKeyExpectation F(
        string dependentEntity,
        string principalEntity,
        string property,
        bool unique = false) => new(dependentEntity, principalEntity, property, unique);
}

internal sealed record IndexExpectation(
    string EntityName,
    string DatabaseName,
    bool Unique,
    string? Filter,
    string[] Properties);

internal sealed record ForeignKeyExpectation(
    string DependentEntity,
    string PrincipalEntity,
    string Property,
    bool Unique);
