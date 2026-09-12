namespace smart_locking_be.Tests;

internal static class ModelContract
{
    internal static readonly string[] EntityNames =
    [
        "AuditLog", "Building", "DeliveryRequest", "EmergencyUnlock", "Incident",
        "IncidentAction", "Locker", "LockerCluster", "LockerCompartment", "LockerEvent",
        "MaintenanceActivity", "MaintenanceRequest", "Notification", "NotificationRule",
        "OperatorAssignment", "OtpChallenge", "OverdueCharge", "Parcel", "ParcelAccessEvent",
        "ParcelStatusHistory", "PaymentTransaction", "Permission", "ResidentBiometric",
        "ResidentProfile", "Role", "RolePermission", "SystemPolicy", "User", "UserRole"
    ];

    internal static readonly IReadOnlyDictionary<string, PropertyExpectation[]> Properties =
        new Dictionary<string, PropertyExpectation[]>
        {
            ["User"] =
            [
                P("Id", "Guid"), P("PhoneNumber", "String", true), P("Email", "String", true),
                P("PasswordHash", "String"), P("Status", "UserStatus"), P("PhoneVerifiedAt", "DateTimeOffset", true),
                P("MustChangePassword", "Boolean"), P("LastLoginAt", "DateTimeOffset", true),
                P("CreatedAt", "DateTimeOffset"), P("UpdatedAt", "DateTimeOffset")
            ],
            ["Role"] =
            [
                P("Id", "Guid"), P("Name", "String"), P("Description", "String", true),
                P("IsSystemRole", "Boolean"), P("IsActive", "Boolean"), P("CreatedAt", "DateTimeOffset"),
                P("UpdatedAt", "DateTimeOffset")
            ],
            ["Permission"] =
            [
                P("Id", "Guid"), P("Code", "String"), P("Name", "String"),
                P("Description", "String", true), P("CreatedAt", "DateTimeOffset")
            ],
            ["UserRole"] =
            [
                P("Id", "Guid"), P("UserId", "Guid"), P("RoleId", "Guid"),
                P("AssignedByUserId", "Guid", true), P("AssignedAt", "DateTimeOffset")
            ],
            ["RolePermission"] =
            [
                P("Id", "Guid"), P("RoleId", "Guid"), P("PermissionId", "Guid"),
                P("CreatedAt", "DateTimeOffset")
            ],
            ["ResidentProfile"] =
            [
                P("Id", "Guid"), P("UserId", "Guid"), P("FullName", "String"), P("DateOfBirth", "DateOnly", true),
                P("AvatarUrl", "String", true), P("DeliveryApprovalMode", "DeliveryApprovalMode"),
                P("PersonalQrTokenHash", "String"), P("PersonalQrIssuedAt", "DateTimeOffset"),
                P("FaceRecognitionEnabled", "Boolean"), P("CreatedAt", "DateTimeOffset"), P("UpdatedAt", "DateTimeOffset")
            ],
            ["ResidentBiometric"] =
            [
                P("Id", "Guid"), P("ResidentProfileId", "Guid"), P("TemplateReference", "String"),
                P("EnrolledAt", "DateTimeOffset"), P("RevokedAt", "DateTimeOffset", true), P("UpdatedAt", "DateTimeOffset")
            ],
            ["OtpChallenge"] =
            [
                P("Id", "Guid"), P("UserId", "Guid", true), P("ParcelId", "Guid", true),
                P("DestinationPhone", "String"), P("Purpose", "OtpPurpose"), P("CodeHash", "String"),
                P("ExpiresAt", "DateTimeOffset"), P("UsedAt", "DateTimeOffset", true),
                P("RevokedAt", "DateTimeOffset", true), P("AttemptCount", "Int32"), P("CreatedAt", "DateTimeOffset")
            ],
            ["Building"] =
            [
                P("Id", "Guid"), P("Code", "String"), P("Name", "String"), P("Address", "String"),
                P("Status", "BuildingStatus"), P("CreatedAt", "DateTimeOffset"), P("UpdatedAt", "DateTimeOffset")
            ],
            ["LockerCluster"] =
            [
                P("Id", "Guid"), P("BuildingId", "Guid"), P("Code", "String"), P("Name", "String"),
                P("LocationDescription", "String", true), P("Status", "LockerClusterStatus"),
                P("CreatedAt", "DateTimeOffset"), P("UpdatedAt", "DateTimeOffset")
            ],
            ["Locker"] =
            [
                P("Id", "Guid"), P("LockerClusterId", "Guid"), P("Code", "String"), P("DeviceIdentifier", "String"),
                P("OperationalStatus", "LockerOperationalStatus"), P("ConnectionStatus", "LockerConnectionStatus"),
                P("LastSeenAt", "DateTimeOffset", true), P("CreatedAt", "DateTimeOffset"), P("UpdatedAt", "DateTimeOffset")
            ],
            ["LockerCompartment"] =
            [
                P("Id", "Guid"), P("LockerId", "Guid"), P("Code", "String"), P("SizeCategory", "String"),
                P("OperationalStatus", "LockerCompartmentOperationalStatus"), P("DoorStatus", "DoorStatus"),
                P("UpdatedAt", "DateTimeOffset"), P("CreatedAt", "DateTimeOffset")
            ],
            ["OperatorAssignment"] =
            [
                P("Id", "Guid"), P("OperatorUserId", "Guid"), P("BuildingId", "Guid", true),
                P("LockerClusterId", "Guid", true), P("LockerId", "Guid", true), P("AssignedByUserId", "Guid"),
                P("AssignedAt", "DateTimeOffset"), P("RevokedAt", "DateTimeOffset", true), P("Reason", "String", true)
            ],
            ["SystemPolicy"] =
            [
                P("Id", "Guid"), P("Version", "Int32"), P("DefaultApprovalMode", "DeliveryApprovalMode"),
                P("DeliveryRequestExpiryMinutes", "Int32"), P("OverdueStartAfterHours", "Int32"),
                P("OverdueFeePerHour", "Decimal"), P("Currency", "String"), P("MaxStorageHours", "Int32"),
                P("ClearanceEligibilityAfterHours", "Int32"), P("ClearanceNoticeBeforeHours", "Int32"),
                P("EnablePersonalQr", "Boolean"), P("EnableOtp", "Boolean"), P("EnableRemoteUnlock", "Boolean"),
                P("EnableFaceRecognition", "Boolean"), P("EffectiveFrom", "DateTimeOffset"),
                P("EffectiveTo", "DateTimeOffset", true), P("IsActive", "Boolean"), P("CreatedByUserId", "Guid"),
                P("CreatedAt", "DateTimeOffset")
            ],
            ["NotificationRule"] =
            [
                P("Id", "Guid"), P("SystemPolicyId", "Guid"), P("EventType", "String"),
                P("Channel", "NotificationChannel"), P("LeadTimeMinutes", "Int32", true), P("IsEnabled", "Boolean")
            ],
            ["DeliveryRequest"] =
            [
                P("Id", "Guid"), P("ResidentProfileId", "Guid"), P("LockerClusterId", "Guid"),
                P("SystemPolicyId", "Guid"), P("AllocatedCompartmentId", "Guid", true),
                P("GuestSessionTokenHash", "String"), P("ShipperName", "String", true), P("ShipperPhone", "String", true),
                P("RecipientPhoneSnapshot", "String"), P("WaybillImageUrl", "String", true),
                P("OcrExtractedPhone", "String", true), P("OcrStatus", "OcrStatus", true), P("SizeCategory", "String"),
                P("ParcelDescription", "String", true), P("ApprovalModeSnapshot", "DeliveryApprovalMode"),
                P("Status", "DeliveryRequestStatus"), P("ExpiresAt", "DateTimeOffset"), P("DecisionAt", "DateTimeOffset", true),
                P("AllocatedAt", "DateTimeOffset", true), P("CompartmentReleasedAt", "DateTimeOffset", true),
                P("FailureCode", "DeliveryRequestFailureCode", true), P("FailureDetail", "String", true),
                P("CreatedAt", "DateTimeOffset"), P("UpdatedAt", "DateTimeOffset")
            ],
            ["Parcel"] =
            [
                P("Id", "Guid"), P("DeliveryRequestId", "Guid"), P("ParcelCode", "String"), P("Status", "ParcelStatus"),
                P("StoredAt", "DateTimeOffset"), P("PickupDueAt", "DateTimeOffset"), P("MaxStorageUntil", "DateTimeOffset"),
                P("RetrievedAt", "DateTimeOffset", true), P("RemovedAt", "DateTimeOffset", true),
                P("RemovedByUserId", "Guid", true), P("RemovalReason", "String", true),
                P("CreatedAt", "DateTimeOffset"), P("UpdatedAt", "DateTimeOffset")
            ],
            ["ParcelStatusHistory"] =
            [
                P("Id", "Guid"), P("ParcelId", "Guid"), P("FromStatus", "ParcelStatus", true),
                P("ToStatus", "ParcelStatus"), P("Reason", "String", true), P("ChangedByUserId", "Guid", true),
                P("ChangedAt", "DateTimeOffset")
            ],
            ["ParcelAccessEvent"] =
            [
                P("Id", "Guid"), P("ParcelId", "Guid"), P("UserId", "Guid", true),
                P("Method", "ParcelAccessMethod"), P("Result", "ParcelAccessResult"),
                P("FailureReason", "String", true), P("IpAddress", "String", true),
                P("DeviceContext", "String", true), P("OccurredAt", "DateTimeOffset")
            ],
            ["OverdueCharge"] =
            [
                P("Id", "Guid"), P("ParcelId", "Guid"), P("Amount", "Decimal"), P("RatePerHourSnapshot", "Decimal"),
                P("Currency", "String"), P("ChargeStartAt", "DateTimeOffset"), P("CalculatedThrough", "DateTimeOffset"),
                P("Status", "OverdueChargeStatus"), P("CreatedAt", "DateTimeOffset"),
                P("UpdatedAt", "DateTimeOffset"), P("PaidAt", "DateTimeOffset", true)
            ],
            ["PaymentTransaction"] =
            [
                P("Id", "Guid"), P("OverdueChargeId", "Guid"), P("Provider", "String"),
                P("ProviderTransactionId", "String", true), P("Amount", "Decimal"), P("Currency", "String"),
                P("Status", "PaymentTransactionStatus"), P("FailureReason", "String", true),
                P("RequestedAt", "DateTimeOffset"), P("CompletedAt", "DateTimeOffset", true)
            ],
            ["Notification"] =
            [
                P("Id", "Guid"), P("UserId", "Guid"), P("Type", "String"), P("Channel", "NotificationChannel"),
                P("Title", "String"), P("Message", "String"), P("ParcelId", "Guid", true),
                P("IncidentId", "Guid", true), P("PaymentTransactionId", "Guid", true),
                P("DeliveryStatus", "NotificationDeliveryStatus"), P("IsRead", "Boolean"),
                P("SentAt", "DateTimeOffset", true), P("ReadAt", "DateTimeOffset", true), P("CreatedAt", "DateTimeOffset")
            ],
            ["Incident"] =
            [
                P("Id", "Guid"), P("ReporterUserId", "Guid", true), P("ReporterName", "String", true),
                P("ReporterPhone", "String", true), P("DeliveryRequestId", "Guid", true), P("ParcelId", "Guid", true),
                P("LockerId", "Guid", true), P("LockerCompartmentId", "Guid", true),
                P("PaymentTransactionId", "Guid", true), P("AssignedOperatorUserId", "Guid", true),
                P("Type", "String"), P("Source", "IncidentSource"), P("Status", "IncidentStatus"),
                P("Title", "String"), P("Description", "String"), P("ResolutionSummary", "String", true),
                P("EscalatedAt", "DateTimeOffset", true), P("ResolvedAt", "DateTimeOffset", true),
                P("CreatedAt", "DateTimeOffset"), P("UpdatedAt", "DateTimeOffset")
            ],
            ["IncidentAction"] =
            [
                P("Id", "Guid"), P("IncidentId", "Guid"), P("ActionByUserId", "Guid"), P("ActionType", "String"),
                P("FromStatus", "IncidentStatus", true), P("ToStatus", "IncidentStatus", true),
                P("Notes", "String", true), P("CreatedAt", "DateTimeOffset")
            ],
            ["LockerEvent"] =
            [
                P("Id", "Guid"), P("LockerId", "Guid"), P("LockerCompartmentId", "Guid", true),
                P("ActorUserId", "Guid", true), P("EventType", "LockerEventType"),
                P("PreviousValue", "String", true), P("NewValue", "String", true),
                P("Severity", "LockerEventSeverity"), P("Reason", "String", true), P("Details", "String", true),
                P("OccurredAt", "DateTimeOffset"), P("ReceivedAt", "DateTimeOffset")
            ],
            ["EmergencyUnlock"] =
            [
                P("Id", "Guid"), P("OperatorUserId", "Guid"), P("LockerId", "Guid"),
                P("LockerCompartmentId", "Guid", true), P("IncidentId", "Guid", true), P("Reason", "String"),
                P("Result", "EmergencyUnlockResult"), P("RequestedAt", "DateTimeOffset"),
                P("CompletedAt", "DateTimeOffset", true)
            ],
            ["MaintenanceRequest"] =
            [
                P("Id", "Guid"), P("LockerId", "Guid"), P("LockerCompartmentId", "Guid", true),
                P("CreatedByUserId", "Guid"), P("IncidentId", "Guid", true), P("Priority", "MaintenancePriority"),
                P("Status", "MaintenanceStatus"), P("Description", "String"),
                P("ResolutionSummary", "String", true), P("CreatedAt", "DateTimeOffset"),
                P("UpdatedAt", "DateTimeOffset"), P("ClosedAt", "DateTimeOffset", true)
            ],
            ["MaintenanceActivity"] =
            [
                P("Id", "Guid"), P("MaintenanceRequestId", "Guid"), P("ActionByUserId", "Guid"),
                P("ActionType", "String"), P("FromStatus", "MaintenanceStatus", true),
                P("ToStatus", "MaintenanceStatus", true), P("Notes", "String", true), P("CreatedAt", "DateTimeOffset")
            ],
            ["AuditLog"] =
            [
                P("Id", "Guid"), P("ActorUserId", "Guid", true), P("Action", "String"),
                P("EntityType", "String", true), P("EntityId", "Guid", true), P("Result", "AuditLogResult"),
                P("IpAddress", "String", true), P("Details", "String", true), P("OccurredAt", "DateTimeOffset")
            ]
        };

    internal static PropertyExpectation P(string name, string typeName, bool nullable = false) =>
        new(name, typeName, nullable);
}

internal sealed record PropertyExpectation(string Name, string TypeName, bool Nullable);
