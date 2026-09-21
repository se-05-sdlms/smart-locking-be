using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Locker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying", maxLength: 50, nullable: false),
                    Address = table.Column<string>(type: "character varying", maxLength: 500, nullable: false),
                    RecoveryAddress = table.Column<string>(type: "character varying", maxLength: 500, nullable: false),
                    DeviceIdentifier = table.Column<string>(type: "character varying", maxLength: 100, nullable: false),
                    OperationalStatus = table.Column<string>(type: "character varying", nullable: false),
                    ConnectionStatus = table.Column<string>(type: "character varying", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locker", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying", maxLength: 254, nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying", nullable: false),
                    Role = table.Column<string>(type: "character varying", nullable: false),
                    PhoneVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                    table.CheckConstraint("CK_User_LoginIdentifier", "\"PhoneNumber\" IS NOT NULL OR \"Email\" IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "LockerCompartment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying", maxLength: 50, nullable: false),
                    HardwareCode = table.Column<string>(type: "character varying", maxLength: 100, nullable: false),
                    HardwareChannel = table.Column<int>(type: "integer", nullable: false),
                    OperationalStatus = table.Column<string>(type: "character varying", nullable: false),
                    DoorStatus = table.Column<string>(type: "character varying", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockerCompartment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LockerCompartment_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying", maxLength: 150, nullable: false),
                    EntityType = table.Column<string>(type: "character varying", maxLength: 150, nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Result = table.Column<string>(type: "character varying", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying", maxLength: 45, nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLog_User_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperatorAssignment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorAssignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatorAssignment_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorAssignment_User_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorAssignment_User_OperatorUserId",
                        column: x => x.OperatorUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshToken",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByIp = table.Column<string>(type: "character varying", maxLength: 45, nullable: true),
                    RevokedByIp = table.Column<string>(type: "character varying", maxLength: 45, nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshToken", x => x.Id);
                    table.CheckConstraint("CK_RefreshToken_ExpirationAndRevocation", "\"ExpiresAt\" > \"CreatedAt\" AND (\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\")");
                    table.ForeignKey(
                        name: "FK_RefreshToken_RefreshToken_ReplacedByTokenId",
                        column: x => x.ReplacedByTokenId,
                        principalTable: "RefreshToken",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefreshToken_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResidentProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying", maxLength: 150, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying", maxLength: 2048, nullable: true),
                    DeliveryApprovalMode = table.Column<string>(type: "character varying", nullable: false),
                    PersonalQrTokenHash = table.Column<string>(type: "character varying", maxLength: 256, nullable: false),
                    PersonalQrIssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FaceRecognitionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidentProfile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResidentProfile_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SystemPolicy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DefaultApprovalMode = table.Column<string>(type: "character varying", nullable: false),
                    GuestSessionTimeoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    ManualApprovalTimeoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    CompartmentReservationMinutes = table.Column<int>(type: "integer", nullable: false),
                    OverdueStartAfterHours = table.Column<int>(type: "integer", nullable: false),
                    OverdueFeePerHour = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying", maxLength: 3, nullable: false),
                    MaxStorageHours = table.Column<int>(type: "integer", nullable: false),
                    ClearanceEligibilityAfterHours = table.Column<int>(type: "integer", nullable: false),
                    ClearanceNoticeBeforeHours = table.Column<int>(type: "integer", nullable: false),
                    OtpMaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    OtpLockoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    EnablePersonalQr = table.Column<bool>(type: "boolean", nullable: false),
                    EnableOtp = table.Column<bool>(type: "boolean", nullable: false),
                    EnableRemoteUnlock = table.Column<bool>(type: "boolean", nullable: false),
                    EnableFaceRecognition = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemPolicy", x => x.Id);
                    table.CheckConstraint("CK_SystemPolicy_DurationsAndRates", "\"GuestSessionTimeoutMinutes\" > 0 AND \"ManualApprovalTimeoutMinutes\" > 0 AND \"CompartmentReservationMinutes\" > 0 AND \"OtpMaxAttempts\" > 0 AND \"OtpLockoutMinutes\" > 0 AND \"OverdueStartAfterHours\" >= 0 AND \"OverdueFeePerHour\" >= 0 AND \"MaxStorageHours\" > 0 AND \"ClearanceEligibilityAfterHours\" >= 0 AND \"ClearanceNoticeBeforeHours\" >= 0");
                    table.ForeignKey(
                        name: "FK_SystemPolicy_User_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LockerEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerCompartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying", nullable: false),
                    PreviousValue = table.Column<string>(type: "character varying", maxLength: 200, nullable: true),
                    NewValue = table.Column<string>(type: "character varying", maxLength: 200, nullable: true),
                    Severity = table.Column<string>(type: "character varying", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockerEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LockerEvent_LockerCompartment_LockerCompartmentId",
                        column: x => x.LockerCompartmentId,
                        principalTable: "LockerCompartment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerEvent_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerEvent_User_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResidentBiometric",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateReference = table.Column<string>(type: "character varying", maxLength: 500, nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidentBiometric", x => x.Id);
                    table.CheckConstraint("CK_ResidentBiometric_RevokedAt", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"EnrolledAt\"");
                    table.ForeignKey(
                        name: "FK_ResidentBiometric_ResidentProfile_ResidentProfileId",
                        column: x => x.ResidentProfileId,
                        principalTable: "ResidentProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemPolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocatedCompartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuestSessionTokenHash = table.Column<string>(type: "character varying", maxLength: 256, nullable: false),
                    ShipperName = table.Column<string>(type: "character varying", maxLength: 150, nullable: true),
                    ShipperPhone = table.Column<string>(type: "character varying", maxLength: 20, nullable: true),
                    RecipientPhoneSnapshot = table.Column<string>(type: "character varying", maxLength: 20, nullable: true),
                    ParcelImageUrl = table.Column<string>(type: "character varying", maxLength: 2048, nullable: true),
                    OcrExtractedPhone = table.Column<string>(type: "character varying", maxLength: 20, nullable: true),
                    OcrStatus = table.Column<string>(type: "character varying", nullable: true),
                    ApprovalModeSnapshot = table.Column<string>(type: "character varying", nullable: true),
                    Status = table.Column<string>(type: "character varying", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SessionExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovalExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReservationExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DepositedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompartmentReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying", nullable: true),
                    FailureDetail = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryRequest", x => x.Id);
                    table.CheckConstraint("CK_DeliveryRequest_AllocatedStatusRequiresCompartment", "\"Status\" NOT IN ('Allocated', 'Deposited') OR \"AllocatedCompartmentId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_DeliveryRequest_LockerCompartment_AllocatedCompartmentId",
                        column: x => x.AllocatedCompartmentId,
                        principalTable: "LockerCompartment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryRequest_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryRequest_ResidentProfile_ResidentProfileId",
                        column: x => x.ResidentProfileId,
                        principalTable: "ResidentProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryRequest_SystemPolicy_SystemPolicyId",
                        column: x => x.SystemPolicyId,
                        principalTable: "SystemPolicy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationRule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemPolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying", maxLength: 100, nullable: false),
                    Channel = table.Column<string>(type: "character varying", nullable: false),
                    LeadTimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRule", x => x.Id);
                    table.CheckConstraint("CK_NotificationRule_LeadTimeMinutes", "\"LeadTimeMinutes\" IS NULL OR \"LeadTimeMinutes\" >= 0");
                    table.ForeignKey(
                        name: "FK_NotificationRule_SystemPolicy_SystemPolicyId",
                        column: x => x.SystemPolicyId,
                        principalTable: "SystemPolicy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Parcel",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParcelCode = table.Column<string>(type: "character varying", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying", nullable: false),
                    StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PickupDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MaxStorageUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RemovalReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parcel", x => x.Id);
                    table.CheckConstraint("CK_Parcel_DeadlineOrder", "\"PickupDueAt\" >= \"StoredAt\" AND \"MaxStorageUntil\" >= \"PickupDueAt\"");
                    table.CheckConstraint("CK_Parcel_TerminalTimestamps", "NOT (\"RetrievedAt\" IS NOT NULL AND \"RemovedAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Parcel_DeliveryRequest_DeliveryRequestId",
                        column: x => x.DeliveryRequestId,
                        principalTable: "DeliveryRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Parcel_User_RemovedByUserId",
                        column: x => x.RemovedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OtpChallenge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParcelId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationPhone = table.Column<string>(type: "character varying", maxLength: 20, nullable: false),
                    Purpose = table.Column<string>(type: "character varying", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpChallenge", x => x.Id);
                    table.CheckConstraint("CK_OtpChallenge_AttemptCount", "\"AttemptCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_OtpChallenge_Parcel_ParcelId",
                        column: x => x.ParcelId,
                        principalTable: "Parcel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OtpChallenge_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OverdueCharge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParcelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RatePerHourSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying", maxLength: 3, nullable: false),
                    ChargeStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CalculatedThrough = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverdueCharge", x => x.Id);
                    table.CheckConstraint("CK_OverdueCharge_AmountsAndPeriod", "\"Amount\" >= 0 AND \"RatePerHourSnapshot\" >= 0 AND \"CalculatedThrough\" >= \"ChargeStartAt\"");
                    table.ForeignKey(
                        name: "FK_OverdueCharge_Parcel_ParcelId",
                        column: x => x.ParcelId,
                        principalTable: "Parcel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ParcelStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParcelId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying", nullable: true),
                    ToStatus = table.Column<string>(type: "character varying", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParcelStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParcelStatusHistory_Parcel_ParcelId",
                        column: x => x.ParcelId,
                        principalTable: "Parcel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ParcelStatusHistory_User_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalParcelId = table.Column<Guid>(type: "uuid", nullable: true),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocatedCompartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReturnCode = table.Column<string>(type: "character varying", maxLength: 100, nullable: false),
                    ReturnReason = table.Column<string>(type: "text", nullable: true),
                    ReturnImageUrl = table.Column<string>(type: "character varying", maxLength: 2048, nullable: true),
                    ShipperPhone = table.Column<string>(type: "character varying", maxLength: 20, nullable: true),
                    ShipperSessionTokenHash = table.Column<string>(type: "character varying", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReservationExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResidentDepositedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ShipperPickedUpAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompartmentReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnRequest", x => x.Id);
                    table.CheckConstraint("CK_ReturnRequest_CompartmentReleasedAt", "\"CompartmentReleasedAt\" IS NULL OR \"AllocatedAt\" IS NULL OR \"CompartmentReleasedAt\" >= \"AllocatedAt\"");
                    table.CheckConstraint("CK_ReturnRequest_ReservationExpiresAt", "\"ReservationExpiresAt\" IS NULL OR \"AllocatedAt\" IS NULL OR \"ReservationExpiresAt\" > \"AllocatedAt\"");
                    table.CheckConstraint("CK_ReturnRequest_ResidentDepositedAt", "\"ResidentDepositedAt\" IS NULL OR \"AllocatedAt\" IS NULL OR \"ResidentDepositedAt\" >= \"AllocatedAt\"");
                    table.CheckConstraint("CK_ReturnRequest_ShipperPickedUpAt", "\"ShipperPickedUpAt\" IS NULL OR \"ResidentDepositedAt\" IS NULL OR \"ShipperPickedUpAt\" >= \"ResidentDepositedAt\"");
                    table.ForeignKey(
                        name: "FK_ReturnRequest_LockerCompartment_AllocatedCompartmentId",
                        column: x => x.AllocatedCompartmentId,
                        principalTable: "LockerCompartment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnRequest_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnRequest_Parcel_OriginalParcelId",
                        column: x => x.OriginalParcelId,
                        principalTable: "Parcel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnRequest_ResidentProfile_ResidentProfileId",
                        column: x => x.ResidentProfileId,
                        principalTable: "ResidentProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTransaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OverdueChargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalOrderCode = table.Column<string>(type: "character varying", maxLength: 150, nullable: true),
                    ExternalTransactionId = table.Column<string>(type: "character varying", maxLength: 150, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransaction", x => x.Id);
                    table.CheckConstraint("CK_PaymentTransaction_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_PaymentTransaction_OverdueCharge_OverdueChargeId",
                        column: x => x.OverdueChargeId,
                        principalTable: "OverdueCharge",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompartmentReservation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerCompartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReturnRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompartmentReservation", x => x.Id);
                    table.CheckConstraint("CK_CompartmentReservation_ExactlyOneOwner", "(\"DeliveryRequestId\" IS NOT NULL AND \"ReturnRequestId\" IS NULL) OR (\"DeliveryRequestId\" IS NULL AND \"ReturnRequestId\" IS NOT NULL)");
                    table.CheckConstraint("CK_CompartmentReservation_ExpiresAfterReserved", "\"ExpiresAt\" > \"ReservedAt\"");
                    table.CheckConstraint("CK_CompartmentReservation_ReleasedAfterReserved", "\"ReleasedAt\" IS NULL OR \"ReleasedAt\" >= \"ReservedAt\"");
                    table.ForeignKey(
                        name: "FK_CompartmentReservation_DeliveryRequest_DeliveryRequestId",
                        column: x => x.DeliveryRequestId,
                        principalTable: "DeliveryRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompartmentReservation_LockerCompartment_LockerCompartmentId",
                        column: x => x.LockerCompartmentId,
                        principalTable: "LockerCompartment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompartmentReservation_ReturnRequest_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LockerAccessEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerCompartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParcelId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReturnRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccessType = table.Column<string>(type: "character varying", nullable: false),
                    AccessMethod = table.Column<string>(type: "character varying", nullable: false),
                    Result = table.Column<string>(type: "character varying", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying", maxLength: 45, nullable: true),
                    DeviceContext = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockerAccessEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LockerAccessEvent_DeliveryRequest_DeliveryRequestId",
                        column: x => x.DeliveryRequestId,
                        principalTable: "DeliveryRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerAccessEvent_LockerCompartment_LockerCompartmentId",
                        column: x => x.LockerCompartmentId,
                        principalTable: "LockerCompartment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerAccessEvent_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerAccessEvent_Parcel_ParcelId",
                        column: x => x.ParcelId,
                        principalTable: "Parcel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerAccessEvent_ReturnRequest_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerAccessEvent_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Incident",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReporterName = table.Column<string>(type: "character varying", maxLength: 150, nullable: true),
                    ReporterPhone = table.Column<string>(type: "character varying", maxLength: 20, nullable: true),
                    DeliveryRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParcelId = table.Column<Guid>(type: "uuid", nullable: true),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: true),
                    LockerCompartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedOperatorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "character varying", nullable: false),
                    Status = table.Column<string>(type: "character varying", nullable: false),
                    Title = table.Column<string>(type: "character varying", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ResolutionSummary = table.Column<string>(type: "text", nullable: true),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incident", x => x.Id);
                    table.CheckConstraint("CK_Incident_ResolvedAt", "\"Status\" <> 'Resolved' OR \"ResolvedAt\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Incident_DeliveryRequest_DeliveryRequestId",
                        column: x => x.DeliveryRequestId,
                        principalTable: "DeliveryRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Incident_LockerCompartment_LockerCompartmentId",
                        column: x => x.LockerCompartmentId,
                        principalTable: "LockerCompartment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Incident_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Incident_Parcel_ParcelId",
                        column: x => x.ParcelId,
                        principalTable: "Parcel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Incident_PaymentTransaction_PaymentTransactionId",
                        column: x => x.PaymentTransactionId,
                        principalTable: "PaymentTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Incident_User_AssignedOperatorUserId",
                        column: x => x.AssignedOperatorUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Incident_User_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyUnlock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerCompartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Result = table.Column<string>(type: "character varying", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyUnlock", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencyUnlock_Incident_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmergencyUnlock_LockerCompartment_LockerCompartmentId",
                        column: x => x.LockerCompartmentId,
                        principalTable: "LockerCompartment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmergencyUnlock_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmergencyUnlock_User_OperatorUserId",
                        column: x => x.OperatorUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IncidentAction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<string>(type: "character varying", maxLength: 100, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying", nullable: true),
                    ToStatus = table.Column<string>(type: "character varying", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentAction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentAction_Incident_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncidentAction_User_ActionByUserId",
                        column: x => x.ActionByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerCompartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Priority = table.Column<string>(type: "character varying", nullable: false),
                    Status = table.Column<string>(type: "character varying", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ResolutionSummary = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRequest", x => x.Id);
                    table.CheckConstraint("CK_MaintenanceRequest_ClosedAt", "\"Status\" <> 'Closed' OR \"ClosedAt\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_MaintenanceRequest_Incident_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequest_LockerCompartment_LockerCompartmentId",
                        column: x => x.LockerCompartmentId,
                        principalTable: "LockerCompartment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequest_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequest_User_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying", maxLength: 100, nullable: false),
                    Channel = table.Column<string>(type: "character varying", nullable: false),
                    Title = table.Column<string>(type: "character varying", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    ParcelId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryStatus = table.Column<string>(type: "character varying", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notification", x => x.Id);
                    table.CheckConstraint("CK_Notification_ReadState", "\"ReadAt\" IS NULL OR \"IsRead\" = TRUE");
                    table.ForeignKey(
                        name: "FK_Notification_Incident_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notification_Parcel_ParcelId",
                        column: x => x.ParcelId,
                        principalTable: "Parcel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notification_PaymentTransaction_PaymentTransactionId",
                        column: x => x.PaymentTransactionId,
                        principalTable: "PaymentTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notification_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceActivity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaintenanceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<string>(type: "character varying", maxLength: 100, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying", nullable: true),
                    ToStatus = table.Column<string>(type: "character varying", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceActivity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceActivity_MaintenanceRequest_MaintenanceRequestId",
                        column: x => x.MaintenanceRequestId,
                        principalTable: "MaintenanceRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceActivity_User_ActionByUserId",
                        column: x => x.ActionByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_Actor_Action_OccurredAt",
                table: "AuditLog",
                columns: new[] { "ActorUserId", "Action", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_EntityType_EntityId",
                table: "AuditLog",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_OccurredAt",
                table: "AuditLog",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_CompartmentReservation_DeliveryRequestId",
                table: "CompartmentReservation",
                column: "DeliveryRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CompartmentReservation_ReturnRequestId",
                table: "CompartmentReservation",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "UX_CompartmentReservation_Active",
                table: "CompartmentReservation",
                column: "LockerCompartmentId",
                unique: true,
                filter: "\"ReleasedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRequest_Locker_Status",
                table: "DeliveryRequest",
                columns: new[] { "LockerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRequest_Resident_Status",
                table: "DeliveryRequest",
                columns: new[] { "ResidentProfileId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRequest_SessionExpiresAt_Status",
                table: "DeliveryRequest",
                columns: new[] { "SessionExpiresAt", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_DeliveryRequest_GuestSessionTokenHash",
                table: "DeliveryRequest",
                column: "GuestSessionTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyUnlock_IncidentId",
                table: "EmergencyUnlock",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyUnlock_Locker_RequestedAt",
                table: "EmergencyUnlock",
                columns: new[] { "LockerId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyUnlock_Operator_RequestedAt",
                table: "EmergencyUnlock",
                columns: new[] { "OperatorUserId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Incident_CreatedAt",
                table: "Incident",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Incident_LockerId_Status",
                table: "Incident",
                columns: new[] { "LockerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Incident_ParcelId",
                table: "Incident",
                column: "ParcelId");

            migrationBuilder.CreateIndex(
                name: "IX_Incident_Status_AssignedOperator",
                table: "Incident",
                columns: new[] { "Status", "AssignedOperatorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAction_ActionByUserId_CreatedAt",
                table: "IncidentAction",
                columns: new[] { "ActionByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAction_IncidentId_CreatedAt",
                table: "IncidentAction",
                columns: new[] { "IncidentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Locker_ConnectionStatus_LastSeenAt",
                table: "Locker",
                columns: new[] { "ConnectionStatus", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Locker_OperationalStatus",
                table: "Locker",
                column: "OperationalStatus");

            migrationBuilder.CreateIndex(
                name: "UX_Locker_Code",
                table: "Locker",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Locker_DeviceIdentifier",
                table: "Locker",
                column: "DeviceIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LockerAccessEvent_Compartment_OccurredAt",
                table: "LockerAccessEvent",
                columns: new[] { "LockerCompartmentId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LockerAccessEvent_DeliveryRequestId",
                table: "LockerAccessEvent",
                column: "DeliveryRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerAccessEvent_Locker_OccurredAt",
                table: "LockerAccessEvent",
                columns: new[] { "LockerId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LockerAccessEvent_ParcelId",
                table: "LockerAccessEvent",
                column: "ParcelId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerAccessEvent_ReturnRequestId",
                table: "LockerAccessEvent",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerAccessEvent_User_OccurredAt",
                table: "LockerAccessEvent",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LockerCompartment_DoorStatus",
                table: "LockerCompartment",
                column: "DoorStatus");

            migrationBuilder.CreateIndex(
                name: "IX_LockerCompartment_OperationalStatus",
                table: "LockerCompartment",
                column: "OperationalStatus");

            migrationBuilder.CreateIndex(
                name: "UX_LockerCompartment_LockerId_Code",
                table: "LockerCompartment",
                columns: new[] { "LockerId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LockerCompartment_LockerId_HardwareCode",
                table: "LockerCompartment",
                columns: new[] { "LockerId", "HardwareCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LockerCompartment_LockerId_HardwareChannel",
                table: "LockerCompartment",
                columns: new[] { "LockerId", "HardwareChannel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LockerEvent_CompartmentId_OccurredAt",
                table: "LockerEvent",
                columns: new[] { "LockerCompartmentId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LockerEvent_EventType_OccurredAt",
                table: "LockerEvent",
                columns: new[] { "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LockerEvent_LockerId_OccurredAt",
                table: "LockerEvent",
                columns: new[] { "LockerId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActivity_Request_CreatedAt",
                table: "MaintenanceActivity",
                columns: new[] { "MaintenanceRequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActivity_User_CreatedAt",
                table: "MaintenanceActivity",
                columns: new[] { "ActionByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequest_CreatedAt",
                table: "MaintenanceRequest",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequest_LockerId_Status",
                table: "MaintenanceRequest",
                columns: new[] { "LockerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequest_Status_Priority",
                table: "MaintenanceRequest",
                columns: new[] { "Status", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_Notification_Type_CreatedAt",
                table: "Notification",
                columns: new[] { "Type", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notification_UserId_IsRead_CreatedAt",
                table: "Notification",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRule_EventType_IsEnabled",
                table: "NotificationRule",
                columns: new[] { "EventType", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "UX_NotificationRule_Policy_Event_Channel",
                table: "NotificationRule",
                columns: new[] { "SystemPolicyId", "EventType", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatorAssignment_Operator_RevokedAt",
                table: "OperatorAssignment",
                columns: new[] { "OperatorUserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_OperatorAssignment_ActiveLocker",
                table: "OperatorAssignment",
                columns: new[] { "OperatorUserId", "LockerId" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenge_Destination_Purpose_CreatedAt",
                table: "OtpChallenge",
                columns: new[] { "DestinationPhone", "Purpose", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenge_ParcelId",
                table: "OtpChallenge",
                column: "ParcelId");

            migrationBuilder.CreateIndex(
                name: "IX_OverdueCharge_Status",
                table: "OverdueCharge",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_OverdueCharge_ParcelId",
                table: "OverdueCharge",
                column: "ParcelId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parcel_Status_MaxStorageUntil",
                table: "Parcel",
                columns: new[] { "Status", "MaxStorageUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_Parcel_Status_PickupDueAt",
                table: "Parcel",
                columns: new[] { "Status", "PickupDueAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Parcel_DeliveryRequestId",
                table: "Parcel",
                column: "DeliveryRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Parcel_ParcelCode",
                table: "Parcel",
                column: "ParcelCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParcelStatusHistory_ParcelId_ChangedAt",
                table: "ParcelStatusHistory",
                columns: new[] { "ParcelId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ParcelStatusHistory_ToStatus_ChangedAt",
                table: "ParcelStatusHistory",
                columns: new[] { "ToStatus", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransaction_Charge_Status",
                table: "PaymentTransaction",
                columns: new[] { "OverdueChargeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransaction_RequestedAt",
                table: "PaymentTransaction",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentTransaction_ExternalOrderCode",
                table: "PaymentTransaction",
                column: "ExternalOrderCode",
                unique: true,
                filter: "\"ExternalOrderCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentTransaction_ExternalTransactionId",
                table: "PaymentTransaction",
                column: "ExternalTransactionId",
                unique: true,
                filter: "\"ExternalTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_UserId_ExpiresAt",
                table: "RefreshToken",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "UX_RefreshToken_TokenHash",
                table: "RefreshToken",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResidentBiometric_TemplateReference",
                table: "ResidentBiometric",
                column: "TemplateReference");

            migrationBuilder.CreateIndex(
                name: "UX_ResidentBiometric_ActiveResident",
                table: "ResidentBiometric",
                column: "ResidentProfileId",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ResidentProfile_DeliveryApprovalMode",
                table: "ResidentProfile",
                column: "DeliveryApprovalMode");

            migrationBuilder.CreateIndex(
                name: "UX_ResidentProfile_PersonalQrTokenHash",
                table: "ResidentProfile",
                column: "PersonalQrTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ResidentProfile_UserId",
                table: "ResidentProfile",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequest_Locker_Status",
                table: "ReturnRequest",
                columns: new[] { "LockerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequest_Resident_Status",
                table: "ReturnRequest",
                columns: new[] { "ResidentProfileId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ReturnRequest_ReturnCode",
                table: "ReturnRequest",
                column: "ReturnCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemPolicy_EffectiveFrom",
                table: "SystemPolicy",
                column: "EffectiveFrom");

            migrationBuilder.CreateIndex(
                name: "UX_SystemPolicy_OneActive",
                table: "SystemPolicy",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "UX_SystemPolicy_Version",
                table: "SystemPolicy",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_Role",
                table: "User",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_User_Status",
                table: "User",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_User_Email",
                table: "User",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_User_PhoneNumber",
                table: "User",
                column: "PhoneNumber",
                unique: true,
                filter: "\"PhoneNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "CompartmentReservation");

            migrationBuilder.DropTable(
                name: "EmergencyUnlock");

            migrationBuilder.DropTable(
                name: "IncidentAction");

            migrationBuilder.DropTable(
                name: "LockerAccessEvent");

            migrationBuilder.DropTable(
                name: "LockerEvent");

            migrationBuilder.DropTable(
                name: "MaintenanceActivity");

            migrationBuilder.DropTable(
                name: "Notification");

            migrationBuilder.DropTable(
                name: "NotificationRule");

            migrationBuilder.DropTable(
                name: "OperatorAssignment");

            migrationBuilder.DropTable(
                name: "OtpChallenge");

            migrationBuilder.DropTable(
                name: "ParcelStatusHistory");

            migrationBuilder.DropTable(
                name: "RefreshToken");

            migrationBuilder.DropTable(
                name: "ResidentBiometric");

            migrationBuilder.DropTable(
                name: "ReturnRequest");

            migrationBuilder.DropTable(
                name: "MaintenanceRequest");

            migrationBuilder.DropTable(
                name: "Incident");

            migrationBuilder.DropTable(
                name: "PaymentTransaction");

            migrationBuilder.DropTable(
                name: "OverdueCharge");

            migrationBuilder.DropTable(
                name: "Parcel");

            migrationBuilder.DropTable(
                name: "DeliveryRequest");

            migrationBuilder.DropTable(
                name: "LockerCompartment");

            migrationBuilder.DropTable(
                name: "ResidentProfile");

            migrationBuilder.DropTable(
                name: "SystemPolicy");

            migrationBuilder.DropTable(
                name: "Locker");

            migrationBuilder.DropTable(
                name: "User");
        }
    }
}
