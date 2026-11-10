using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using System.Text.Json;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? throw new InvalidOperationException("Set ConnectionStrings__DefaultConnection.");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql(connectionString).Options);
await db.Database.OpenConnectionAsync();
var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).Distinct().Order().ToArray();
bool createdDataset = false;
if (tables.Length != 41) throw new Exception($"Expected 41 project tables, found {tables.Length}; review the dataset.");
if (args.Length != 1 || args[0] is not ("--verify" or "--dry-run" or "--apply"))
    throw new ArgumentException("Use --verify, --dry-run (rollback), or --apply (commit).");
if (args[0] != "--verify")
{
    var password = Environment.GetEnvironmentVariable("BOXORA_DEMO_PASSWORD");
    if (string.IsNullOrEmpty(password) || password.Length < 16)
        throw new ArgumentException("Set BOXORA_DEMO_PASSWORD to a private password of at least 16 characters.");
    await using var transaction = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(20261110, 41)");
    var adminId = DemoId(1, 0);
    if (await db.Users.AnyAsync(user => user.Id == adminId))
    {
        await VerifyAsync(db, tables);
        Console.WriteLine("Dataset already exists; verified without inserting duplicates.");
    }
    else
    {
        createdDataset = true;
        await SeedAsync(db, password);
        await db.SaveChangesAsync();
        await VerifyAsync(db, tables);
        if (args[0] == "--apply")
        {
            await transaction.CommitAsync();
            Console.WriteLine("COMMITTED demo dataset.");
        }
        else Console.WriteLine("DRY RUN passed; rolling back all inserts.");
    }
}
db.ChangeTracker.Clear();
if (args[0] == "--dry-run")
{
    var adminId = DemoId(1, 0);
    if (createdDataset && await db.Users.AnyAsync(user => user.Id == adminId))
        throw new Exception("Dry-run rollback verification failed.");
    Console.WriteLine("PASS: dry run left no demo admin in the database.");
    return;
}
var counts = await VerifyAsync(db, tables);
Console.WriteLine($"Verified {counts.Count} project tables, {counts.Values.Sum()} total rows.");
var reportPath = Environment.GetEnvironmentVariable("BOXORA_DEMO_REPORT");
if (!string.IsNullOrWhiteSpace(reportPath))
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { checkedAt = DateTimeOffset.UtcNow, counts }, new JsonSerializerOptions { WriteIndented = true }));

static async Task<SortedDictionary<string, long>> VerifyAsync(ApplicationDbContext db, string[] tables)
{
    var counts = new SortedDictionary<string, long>();
    await using var command = new NpgsqlCommand(string.Join(" UNION ALL ", tables.Select(table =>
        $"SELECT '{table}' AS name, count(*) FROM public.\"{table}\"")),
        (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync()) counts[reader.GetString(0)] = reader.GetInt64(1);
    }
    foreach (var item in counts) Console.WriteLine($"{item.Key}: {item.Value}");
    var missing = counts.Where(item => item.Value < 10).Select(item => $"{item.Key}={item.Value}").ToArray();
    if (missing.Length > 0) throw new Exception("Tables below 10 rows: " + string.Join(", ", missing));
    await VerifyRelationshipsAsync(db);
    return counts;
}

static Guid DemoId(int group, int row) => Guid.Parse($"de202611-1000-4000-8000-{group:0000}{row:00000000}");

static async Task SeedAsync(ApplicationDbContext db, string password)
{
    var now = DateTimeOffset.UtcNow;
    var created = now.AddDays(-150);
    var hashes = new Pbkdf2PasswordHashService();
    var tokens = new Sha256TokenHashService();
    User MakeUser(int group, int row, string email, UserRole role) => new() {
        Id = DemoId(group, row), Email = email, PhoneNumber = $"09000{group}{row:0000}",
        PasswordHash = hashes.HashPassword(password), Role = role, Status = UserStatus.Active,
        PhoneVerifiedAt = created, CreatedAt = created, UpdatedAt = created
    };
    var admin = MakeUser(1, 0, "demo.admin@boxora.example", UserRole.Administrator);
    db.Add(admin);
    var existingActivePolicy = await db.SystemPolicies.SingleOrDefaultAsync(policy => policy.IsActive);
    int lastVersion = await db.SystemPolicies.Select(policy => (int?)policy.Version).MaxAsync() ?? 0;
    for (int i = 1; i <= 10; i++)
    {
        var policy = new SystemPolicy {
            Id = DemoId(10, i), Version = lastVersion + i, DefaultApprovalMode = DeliveryApprovalMode.Auto,
            GuestSessionTimeoutMinutes = 30, ManualApprovalTimeoutMinutes = 15, CompartmentReservationMinutes = 10,
            OverdueStartAfterHours = 48, OverdueFeePerHour = 2000, Currency = "VND", MaxStorageHours = 240,
            ClearanceEligibilityAfterHours = 240, ClearanceNoticeBeforeHours = 24, PickupReminderStartDay = 5,
            OtpMaxAttempts = 5, OtpLockoutMinutes = 15, EnableOtp = true, EnableRemoteUnlock = true,
            EnableBluetoothUnlock = false, OperatorReplacementRadiusKm = 10, OperatorReplacementMaxLoad = 5,
            EffectiveFrom = now.AddDays(-120 + i * 10), EffectiveTo = i == 10 ? null : now.AddDays(-110 + i * 10),
            IsActive = i == 10 && existingActivePolicy is null, CreatedByUserId = admin.Id, CreatedAt = created
        };
        db.Add(policy);
        db.Add(new NotificationRule {
            Id = DemoId(11, i), SystemPolicyId = policy.Id, EventType = "ParcelStored",
            Channel = NotificationChannel.InApp, IsEnabled = true
        });
    }
    var activePolicyId = existingActivePolicy?.Id ?? DemoId(10, 10);
    string[] names = ["Nguyễn Minh An", "Trần Ngọc Bình", "Lê Thanh Chi", "Phạm Quốc Dũng", "Hoàng Mai Hà",
        "Vũ Gia Huy", "Đặng Khánh Linh", "Bùi Anh Nam", "Đỗ Bảo Ngọc", "Phan Minh Tú"];
    for (int i = 1; i <= 10; i++)
    {
        var historical = now.AddDays(-12).AddHours(i);
        var recent = now.AddHours(-1).AddMinutes(i);
        var resident = MakeUser(2, i, $"demo.resident{i:00}@boxora.example", UserRole.Resident);
        var op = MakeUser(3, i, $"demo.operator{i:00}@boxora.example", UserRole.LockerOperator);
        var backup = MakeUser(4, i, $"demo.backup{i:00}@boxora.example", UserRole.LockerOperator);
        db.AddRange(resident, op, backup);
        var locker = new Locker {
            Id = DemoId(20, i), Code = $"DEMO-LK-{i:00}", Address = $"Sảnh tòa DEMO {i:00}, khu dân cư mẫu, TP. Hồ Chí Minh",
            RecoveryAddress = $"Quầy hỗ trợ DEMO {i:00}, tầng trệt", DeviceIdentifier = $"DEMO-DEVICE-{i:00}",
            OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Offline,
            Latitude = 10.77 + i * 0.001, Longitude = 106.69 + i * 0.001, ServiceArea = "DEMO-HCM",
            LocationConfirmedByUserId = admin.Id, LocationConfirmedAt = created.AddDays(1), LocationRevision = DemoId(21, i),
            CreatedAt = created, UpdatedAt = created.AddDays(1)
        };
        db.Add(locker);
        for (int channel = 1; channel <= 10; channel++)
            db.Add(new LockerCompartment {
                Id = DemoId(30 + channel, i), LockerId = locker.Id, Code = $"C{channel:00}",
                HardwareCode = $"DEMO-CH-{channel:00}", HardwareChannel = channel,
                OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed,
                DoorStatusObservedAt = now.AddMinutes(-5), CreatedAt = created, UpdatedAt = now.AddMinutes(-5)
            });
        db.Add(new LockerCommissioning {
            LockerId = locker.Id, Stage = LockerCommissioningStage.Declared, Revision = 1,
            CreatedAt = created, UpdatedAt = created
        });
        db.Add(new LockerSupportSettings {
            LockerId = locker.Id, FallbackName = "Quầy hỗ trợ DEMO", FallbackPhoneNumber = op.PhoneNumber,
            FallbackHours = "08:00–18:00", FallbackInstructions = "Dữ liệu mẫu: liên hệ ban quản lý tại sảnh.",
            UpdatedByUserId = admin.Id, UpdatedAt = created
        });
        foreach (var contact in new[] { op, backup })
            db.Add(new OperatorSupportContact {
                OperatorUserId = contact.Id, DisplayName = $"DEMO vận hành {i:00}" + (contact == backup ? " dự phòng" : ""),
                WorkPhoneNumber = contact.PhoneNumber!, IsPublic = true, UtcOffsetMinutes = 420, WeekdaysMask = 127,
                StartsAtMinute = 480, EndsAtMinute = 1080, Instructions = "Dữ liệu liên hệ mẫu, không gọi số này.",
                UpdatedByUserId = admin.Id, UpdatedAt = created
            });
        var assignment = new OperatorAssignment {
            Id = DemoId(41, i), LockerId = locker.Id, OperatorUserId = op.Id, AssignedByUserId = admin.Id,
            AssignedAt = created.AddDays(2), Reason = "DEMO: phân công vận hành chính"
        };
        db.Add(assignment);
        var absence = new OperatorAbsence {
            Id = DemoId(42, i), OperatorUserId = op.Id, CreatedByUserId = admin.Id,
            StartsAt = historical.AddDays(-2), EndsAt = historical.AddDays(-1),
            Reason = "DEMO: nghỉ phép đã kết thúc", CreatedAt = historical.AddDays(-3)
        };
        db.Add(absence);
        db.Add(new TemporaryOperatorAssignment {
            Id = DemoId(43, i), AbsenceId = absence.Id, BaseAssignmentId = assignment.Id, LockerId = locker.Id,
            OperatorUserId = backup.Id, AnchorLockerId = locker.Id, TargetLocationRevision = locker.LocationRevision,
            AnchorLocationRevision = locker.LocationRevision, DistanceKm = 0, LoadBefore = 0, LoadAfter = 1,
            ServiceArea = locker.ServiceArea, PolicyId = activePolicyId, SelectionFingerprint = tokens.HashToken($"demo-coverage-{i}"),
            Status = TemporaryAssignmentStatus.Expired, StatusReason = "DEMO: đã hoàn tất ca thay thế",
            ApprovedByUserId = admin.Id, ApprovedAt = absence.CreatedAt.AddHours(1), ActivatedAt = absence.StartsAt,
            CompletedAt = absence.EndsAt, CreatedAt = absence.CreatedAt
        });
        var profile = new ResidentProfile {
            Id = DemoId(50, i), UserId = resident.Id, RegisteredLockerId = locker.Id, FullName = names[i - 1],
            DateOfBirth = new DateOnly(1990 + i, i, 10), DeliveryApprovalMode = i % 2 == 0 ? DeliveryApprovalMode.Manual : DeliveryApprovalMode.Auto,
            FaceRecognitionEnabled = false, CreatedAt = created.AddDays(3), UpdatedAt = created.AddDays(3)
        };
        db.Add(profile);
        db.Add(new ResidentBiometric {
            Id = DemoId(51, i), ResidentProfileId = profile.Id, TemplateReference = $"DEMO-REVOKED-NOT-A-REAL-BIOMETRIC-{i:00}",
            EnrolledAt = historical.AddDays(-3), RevokedAt = historical.AddDays(-2), UpdatedAt = historical.AddDays(-2)
        });
        var plan = new SubscriptionPlan {
            Id = DemoId(52, i), Code = $"DEMO-PLAN-{i:00}", Name = $"Gói DEMO {i:00}", Description = "Gói cước mẫu để kiểm thử giao nhận",
            Price = 30000 + i * 10000, DurationDays = 30, MaxActiveParcels = 5 + i, IsActive = true,
            CreatedAt = created, UpdatedAt = created
        };
        db.Add(plan);
        db.Add(new ResidentSubscription {
            Id = DemoId(53, i), ResidentProfileId = profile.Id, SubscriptionPlanId = plan.Id,
            StartDate = now.AddDays(-15), EndDate = now.AddDays(15), Status = SubscriptionStatus.Active,
            PriceSnapshot = plan.Price, PlanCodeSnapshot = plan.Code, PlanNameSnapshot = plan.Name,
            DurationDaysSnapshot = plan.DurationDays, MaxActiveParcelsSnapshot = plan.MaxActiveParcels,
            BenefitsSnapshotRecordedAt = now.AddDays(-15), PaidAt = now.AddDays(-15),
            CreatedAt = now.AddDays(-15), UpdatedAt = now.AddDays(-15)
        });
        // Historical retrieved parcels exercise paid fees; current stored parcels use separate compartments.
        for (int scenario = 0; scenario <= 1; scenario++)
        {
            int row = i + scenario * 10;
            var stored = scenario == 0 ? historical : recent;
            var compartmentId = DemoId(31 + scenario, i);
            var delivery = new DeliveryRequest {
                Id = DemoId(60, row), ResidentProfileId = profile.Id, LockerId = locker.Id, SystemPolicyId = activePolicyId,
                AllocatedCompartmentId = compartmentId, GuestSessionTokenHash = tokens.HashToken(tokens.CreateSecureToken()),
                RecipientPhoneSnapshot = resident.PhoneNumber, OcrStatus = OcrStatus.Skipped, ApprovalModeSnapshot = profile.DeliveryApprovalMode,
                Status = DeliveryRequestStatus.Deposited, CreatedAt = stored.AddMinutes(-10), UpdatedAt = stored,
                DecisionAt = stored.AddMinutes(-8), AllocatedAt = stored.AddMinutes(-5), DepositedAt = stored,
                LastActivityAt = stored, SessionExpiresAt = stored.AddMinutes(20), ReservationExpiresAt = stored.AddMinutes(5),
                CompartmentReleasedAt = scenario == 0 ? stored.AddHours(50) : null
            };
            db.Add(delivery);
            var parcel = new Parcel {
                Id = DemoId(61, row), DeliveryRequestId = delivery.Id, ParcelCode = $"DEMO-PARCEL-{row:000}",
                Status = scenario == 0 ? ParcelStatus.Retrieved : ParcelStatus.Stored, StoredAt = stored,
                PickupDueAt = stored.AddHours(48), MaxStorageUntil = stored.AddHours(240),
                RetrievedAt = scenario == 0 ? stored.AddHours(50) : null,
                CreatedAt = stored, UpdatedAt = scenario == 0 ? stored.AddHours(50) : stored
            };
            db.Add(parcel);
            db.Add(new CompartmentReservation {
                Id = DemoId(62, row), LockerCompartmentId = compartmentId, DeliveryRequestId = delivery.Id,
                ReservedAt = stored.AddMinutes(-5), ExpiresAt = stored.AddMinutes(5),
                ReleasedAt = delivery.CompartmentReleasedAt, CreatedAt = stored.AddMinutes(-5)
            });
            db.Add(new ParcelStatusHistory {
                Id = DemoId(63, row), ParcelId = parcel.Id, ToStatus = ParcelStatus.Stored,
                Reason = "DEMO: xác nhận gửi kiện vào ngăn", ChangedAt = stored
            });
            db.Add(new LockerAccessEvent {
                Id = DemoId(64, row), LockerId = locker.Id, LockerCompartmentId = compartmentId,
                DeliveryRequestId = delivery.Id, ParcelId = parcel.Id, AccessType = LockerAccessType.ShipperDropOff,
                AccessMethod = LockerAccessMethod.GuestSession, Result = LockerAccessResult.Succeeded,
                DeviceContext = "DEMO historical record; no device command sent", OccurredAt = stored.AddMinutes(-2)
            });
            if (scenario == 1) continue;
            db.Add(new ParcelStatusHistory {
                Id = DemoId(65, i), ParcelId = parcel.Id, FromStatus = ParcelStatus.Stored, ToStatus = ParcelStatus.Overdue,
                Reason = "DEMO: quá hạn miễn phí", ChangedAt = stored.AddHours(48)
            });
            db.Add(new ParcelStatusHistory {
                Id = DemoId(66, i), ParcelId = parcel.Id, FromStatus = ParcelStatus.Overdue, ToStatus = ParcelStatus.Retrieved,
                ChangedByUserId = resident.Id, Reason = "DEMO: đã thanh toán và nhận kiện", ChangedAt = parcel.RetrievedAt!.Value
            });
            db.Add(new LockerAccessEvent {
                Id = DemoId(67, i), LockerId = locker.Id, LockerCompartmentId = compartmentId, UserId = resident.Id,
                DeliveryRequestId = delivery.Id, ParcelId = parcel.Id, AccessType = LockerAccessType.ResidentPickup,
                AccessMethod = LockerAccessMethod.RemoteApp, Result = LockerAccessResult.Succeeded,
                DeviceContext = "DEMO historical pickup", OccurredAt = parcel.RetrievedAt!.Value
            });
            var charge = new OverdueCharge {
                Id = DemoId(68, i), ParcelId = parcel.Id, Amount = 4000, RatePerHourSnapshot = 2000, Currency = "VND",
                ChargeStartAt = stored.AddHours(48), CalculatedThrough = stored.AddHours(50), Status = OverdueChargeStatus.Paid,
                PaidAt = stored.AddHours(50).AddMinutes(-1), CreatedAt = stored.AddHours(48), UpdatedAt = stored.AddHours(50).AddMinutes(-1)
            };
            db.Add(charge);
            var payment = new PaymentTransaction {
                Id = DemoId(69, i), OverdueChargeId = charge.Id, Amount = charge.Amount, Currency = charge.Currency,
                Status = PaymentTransactionStatus.Success, RequestedAt = charge.PaidAt!.Value.AddMinutes(-1), CompletedAt = charge.PaidAt
            };
            db.Add(payment);
            var reconciliation = new PaymentReconciliation {
                Id = DemoId(70, i), Reference = $"DEMO-REC-{i:000}", ResidentUserId = resident.Id, PaymentTransactionId = payment.Id,
                Reason = "DEMO: yêu cầu kiểm tra biên nhận mẫu", Status = "Resolved", AssignedAdminUserId = admin.Id,
                Response = "Dữ liệu mô phỏng: khoản phí đã được ghi nhận, không có giao dịch ngân hàng thật.",
                CreatedAt = historical.AddDays(4), UpdatedAt = historical.AddDays(4).AddHours(1)
            };
            db.Add(reconciliation);
            db.Add(new PaymentReconciliationEvent {
                Id = DemoId(71, i), PaymentReconciliationId = reconciliation.Id, ActorUserId = admin.Id,
                Action = "Resolved", Status = "Resolved", Message = reconciliation.Response,
                CreatedAt = reconciliation.UpdatedAt
            });
            db.Add(new OtpChallenge {
                Id = DemoId(72, i), UserId = resident.Id, ParcelId = parcel.Id, DestinationPhone = resident.PhoneNumber!,
                DestinationEmail = resident.Email, Purpose = OtpPurpose.ParcelRetrieval, CodeHash = tokens.HashToken(tokens.CreateNumericCode()),
                CreatedAt = stored.AddHours(49), ExpiresAt = stored.AddHours(49).AddMinutes(5), RevokedAt = stored.AddHours(49).AddMinutes(5)
            });
        }
        bool pendingCancellation = i % 3 == 0;
        bool alreadyCollected = i % 3 == 1;
        var returned = new ReturnRequest {
            Id = DemoId(80, i), ResidentProfileId = profile.Id, OriginalParcelId = DemoId(61, i), LockerId = locker.Id,
            AllocatedCompartmentId = pendingCancellation || alreadyCollected ? DemoId(33, i) : null,
            ReturnCode = $"DEMO-RETURN-{i:000}", ReturnReason = "DEMO: đổi kích cỡ sản phẩm",
            Status = pendingCancellation ? ReturnRequestStatus.Deposited : alreadyCollected ? ReturnRequestStatus.Completed : ReturnRequestStatus.Cancelled,
            CreatedAt = historical.AddDays(5), UpdatedAt = pendingCancellation ? recent : historical.AddDays(5).AddHours(2),
            AllocatedAt = pendingCancellation || alreadyCollected ? historical.AddDays(5).AddMinutes(1) : null,
            ReservationExpiresAt = pendingCancellation || alreadyCollected ? historical.AddDays(5).AddMinutes(11) : null,
            ResidentDepositedAt = pendingCancellation || alreadyCollected ? historical.AddDays(5).AddMinutes(3) : null,
            ShipperPickedUpAt = alreadyCollected ? historical.AddDays(5).AddHours(2) : null,
            CompartmentReleasedAt = alreadyCollected ? historical.AddDays(5).AddHours(2) : null
        };
        db.Add(returned);
        var cancellation = new ReturnCancellationRequest {
            ReturnRequestId = returned.Id, ResidentUserId = resident.Id,
            RequestedAt = pendingCancellation ? recent : historical.AddDays(5).AddHours(1),
            Status = pendingCancellation ? "PendingVerification" : alreadyCollected ? "AlreadyCollected" : "Cancelled",
            CompletedAt = pendingCancellation ? null : returned.UpdatedAt
        };
        db.Add(cancellation);
        if (returned.AllocatedCompartmentId is Guid returnCompartment)
            db.Add(new CompartmentReservation {
                Id = DemoId(81, i), LockerCompartmentId = returnCompartment, ReturnRequestId = returned.Id,
                ReservedAt = returned.AllocatedAt!.Value, ExpiresAt = returned.ReservationExpiresAt!.Value,
                ReleasedAt = returned.CompartmentReleasedAt, CreatedAt = returned.AllocatedAt.Value
            });
        bool closed = i % 2 == 0;
        var incident = new Incident {
            Id = DemoId(90, i), ReporterUserId = resident.Id, ReporterName = names[i - 1], ReporterPhone = resident.PhoneNumber,
            LockerId = locker.Id, LockerCompartmentId = DemoId(39, i), AssignedOperatorUserId = op.Id,
            Type = "Compartment", Source = IncidentSource.Resident, Status = closed ? IncidentStatus.Resolved : IncidentStatus.Investigating,
            Title = $"DEMO {i:00}: kiểm tra đèn báo ngăn C09", Description = "Tình huống mô phỏng: đèn trạng thái ngăn chập chờn.",
            ResolutionSummary = closed ? "DEMO: đã thay đèn và kiểm tra cảm biến." : null,
            ResolvedAt = closed ? historical.AddDays(1) : null, CreatedAt = historical, UpdatedAt = closed ? historical.AddDays(1) : recent
        };
        db.Add(incident);
        db.Add(new IncidentAction {
            Id = DemoId(91, i), IncidentId = incident.Id, ActionByUserId = op.Id,
            ActionType = closed ? "Resolved" : "Investigating", FromStatus = IncidentStatus.Open, ToStatus = incident.Status,
            Notes = "DEMO: nhật ký kiểm tra thiết bị", CreatedAt = incident.UpdatedAt
        });
        var maintenance = new MaintenanceRequest {
            Id = DemoId(92, i), LockerId = locker.Id, LockerCompartmentId = DemoId(39, i), CreatedByUserId = op.Id,
            IncidentId = incident.Id, Priority = MaintenancePriority.Normal,
            Status = closed ? MaintenanceStatus.Closed : MaintenanceStatus.InProgress,
            Description = "DEMO: bảo trì đèn báo và kiểm tra cảm biến cửa C09",
            ResolutionSummary = closed ? incident.ResolutionSummary : null, ClosedAt = incident.ResolvedAt,
            CreatedAt = historical.AddHours(1), UpdatedAt = incident.UpdatedAt
        };
        db.Add(maintenance);
        db.Add(new MaintenanceActivity {
            Id = DemoId(93, i), MaintenanceRequestId = maintenance.Id, ActionByUserId = op.Id,
            ActionType = closed ? "Closed" : "InProgress", FromStatus = MaintenanceStatus.Open, ToStatus = maintenance.Status,
            Notes = "DEMO: ghi nhận kết quả bảo trì", CreatedAt = maintenance.UpdatedAt
        });
        db.Add(new EmergencyUnlock {
            Id = DemoId(94, i), OperatorUserId = op.Id, LockerId = locker.Id, LockerCompartmentId = DemoId(39, i),
            IncidentId = incident.Id, Reason = "DEMO: lịch sử mở cửa kiểm tra cảm biến, không gửi lệnh thật",
            Result = EmergencyUnlockResult.Succeeded, RequestedAt = historical.AddHours(2), CompletedAt = historical.AddHours(2).AddSeconds(3)
        });
        db.Add(new LockerEvent {
            Id = DemoId(95, i), LockerId = locker.Id, LockerCompartmentId = DemoId(39, i), ActorUserId = op.Id,
            EventType = LockerEventType.DoorChanged, PreviousValue = "Open", NewValue = "Closed", Severity = LockerEventSeverity.Info,
            Reason = "DEMO: cửa đã đóng sau kiểm tra", Details = "Bản ghi mô phỏng, không phải telemetry thiết bị thật.",
            OccurredAt = historical.AddHours(2).AddMinutes(1), ReceivedAt = historical.AddHours(2).AddMinutes(1).AddSeconds(1)
        });
        var handover = new OperatorTaskHandover {
            Id = DemoId(96, i), LockerId = locker.Id, TaskKind = "incident", TaskId = incident.Id,
            AuthorUserId = admin.Id, AuthorName = "DEMO quản trị", IntendedReceiverUserId = op.Id,
            WorkDone = "DEMO: hoàn tất kiểm tra ban đầu", Blockers = "", NextSteps = "Theo dõi tình trạng đèn báo.",
            CreatedAt = historical.AddHours(3)
        };
        db.Add(handover);
        db.Add(new OperatorTaskAcknowledgement {
            Id = DemoId(97, i), HandoverId = handover.Id, ReceiverUserId = op.Id, ReceiverName = $"DEMO vận hành {i:00}",
            Notes = "DEMO: đã xác nhận ghi chú công việc.", CreatedAt = handover.CreatedAt.AddMinutes(5)
        });
        db.Add(new ResidentAccountChange {
            Id = DemoId(98, i), ResidentUserId = resident.Id, Kind = "Locker", Status = "Rejected",
            OriginalLockerId = locker.Id, TargetLockerId = DemoId(20, i % 10 + 1),
            Reason = "DEMO: đề nghị đổi locker đăng ký", ReviewedByUserId = admin.Id,
            ReviewReason = "DEMO: yêu cầu thử nghiệm, giữ nguyên locker hiện tại.",
            CreatedAt = historical.AddDays(6), UpdatedAt = historical.AddDays(6).AddHours(1)
        });
        db.Add(new ResidentAccountChangeEvent {
            Id = DemoId(99, i), ResidentAccountChangeId = DemoId(98, i), ActorUserId = admin.Id,
            Action = "Rejected", Message = "DEMO: đã xét duyệt, không thay đổi thông tin thật.", CreatedAt = historical.AddDays(6).AddHours(1)
        });
        db.Add(new DeviceInstallation {
            Id = DemoId(100, i), UserId = resident.Id, InstallationId = $"DEMO-INSTALLATION-{i:00}",
            ExpoPushToken = $"DEMO-INACTIVE-NOT-A-REAL-PUSH-TOKEN-{i:00}", Platform = "android", IsActive = false,
            CreatedAt = historical, UpdatedAt = historical
        });
        db.Add(new RefreshToken {
            Id = DemoId(101, i), UserId = resident.Id, TokenHash = tokens.HashToken(tokens.CreateSecureToken()),
            CreatedAt = historical, ExpiresAt = historical.AddDays(1), RevokedAt = historical.AddHours(1),
            CreatedByIp = "192.0.2.10", RevokedByIp = "192.0.2.10"
        });
        db.Add(new Notification {
            Id = DemoId(102, i), UserId = resident.Id, DeliveryRequestId = DemoId(60, i + 10), ParcelId = DemoId(61, i + 10),
            Type = "ParcelStored", Channel = NotificationChannel.InApp, Title = $"DEMO: kiện hàng {i:00} đã đến",
            Message = $"Kiện DEMO-PARCEL-{i + 10:000} đang ở {locker.Code}, ngăn C02.",
            DeliveryStatus = NotificationDeliveryStatus.Sent, IsRead = i % 2 == 0, SentAt = recent,
            ReadAt = i % 2 == 0 ? recent.AddMinutes(1) : null, CreatedAt = recent
        });
        db.Add(new AuditLog {
            Id = DemoId(103, i), ActorUserId = admin.Id, Action = "DemoData.Seeded", EntityType = nameof(Locker), EntityId = locker.Id,
            Result = AuditLogResult.Succeeded, IpAddress = "192.0.2.10", Details = $"DEMO dataset scenario {i:00}", OccurredAt = now
        });
    }
    foreach (var entry in db.ChangeTracker.Entries<User>())
        if (!hashes.VerifyPassword(password, entry.Entity.PasswordHash) || hashes.VerifyPassword(password + "invalid", entry.Entity.PasswordHash))
            throw new Exception("Password hash self-check failed.");
    var missing = db.Model.GetEntityTypes().Where(type => db.ChangeTracker.Entries().Count(entry => entry.Metadata == type) < 10 && type.ClrType != typeof(User)).Select(type => type.ClrType.Name).ToArray();
    if (missing.Length > 0) throw new Exception("Dataset omits required rows: " + string.Join(", ", missing));
}

static async Task VerifyRelationshipsAsync(ApplicationDbContext db)
{
    var checks = db.Model.GetEntityTypes().SelectMany(type => type.GetForeignKeys()).Select((fk, index) => {
        string present = string.Join(" AND ", fk.Properties.Select(property => $"d.\"{property.Name}\" IS NOT NULL"));
        string join = string.Join(" AND ", fk.Properties.Zip(fk.PrincipalKey.Properties,
            (dependent, principal) => $"d.\"{dependent.Name}\" = p.\"{principal.Name}\""));
        return $"SELECT 'foreign_key_{index}' AS name, count(*) FROM public.\"{fk.DeclaringEntityType.GetTableName()}\" d " +
            $"LEFT JOIN public.\"{fk.PrincipalEntityType.GetTableName()}\" p ON {join} WHERE {present} AND p.\"{fk.PrincipalKey.Properties[0].Name}\" IS NULL";
    }).ToList();
    Dictionary<string, string> businessChecks = new() {
        ["resident_role"] = "SELECT count(*) FROM \"ResidentProfile\" r JOIN \"User\" u ON r.\"UserId\"=u.\"Id\" WHERE u.\"Role\" <> 'Resident'",
        ["operator_role"] = "SELECT count(*) FROM \"OperatorAssignment\" a JOIN \"User\" u ON a.\"OperatorUserId\"=u.\"Id\" WHERE u.\"Role\" <> 'LockerOperator'",
        ["delivery_compartment_locker"] = "SELECT count(*) FROM \"DeliveryRequest\" d JOIN \"LockerCompartment\" c ON d.\"AllocatedCompartmentId\"=c.\"Id\" WHERE d.\"LockerId\"<>c.\"LockerId\"",
        ["return_compartment_locker"] = "SELECT count(*) FROM \"ReturnRequest\" r JOIN \"LockerCompartment\" c ON r.\"AllocatedCompartmentId\"=c.\"Id\" WHERE r.\"LockerId\"<>c.\"LockerId\"",
        ["parcel_terminal_timestamp"] = "SELECT count(*) FROM \"Parcel\" WHERE (\"Status\"='Retrieved' AND \"RetrievedAt\" IS NULL) OR (\"Status\"='Removed' AND \"RemovedAt\" IS NULL) OR (\"Status\" IN ('Stored','Overdue') AND (\"RetrievedAt\" IS NOT NULL OR \"RemovedAt\" IS NOT NULL))",
        ["pickup_release"] = "SELECT count(*) FROM \"Parcel\" p JOIN \"DeliveryRequest\" d ON p.\"DeliveryRequestId\"=d.\"Id\" WHERE p.\"Status\"='Retrieved' AND d.\"CompartmentReleasedAt\" IS DISTINCT FROM p.\"RetrievedAt\"",
        ["paid_charge"] = "SELECT count(*) FROM \"OverdueCharge\" WHERE \"Status\"='Paid' AND \"PaidAt\" IS NULL",
        ["successful_payment"] = "SELECT count(*) FROM \"PaymentTransaction\" p JOIN \"OverdueCharge\" c ON p.\"OverdueChargeId\"=c.\"Id\" WHERE p.\"Status\"='Success' AND (p.\"CompletedAt\" IS NULL OR c.\"Status\"<>'Paid' OR p.\"Amount\"<>c.\"Amount\" OR p.\"Currency\"<>c.\"Currency\")",
        ["subscription_snapshot"] = "SELECT count(*) FROM \"ResidentSubscription\" WHERE \"Status\"='Active' AND (\"StartDate\">=\"EndDate\" OR \"PaidAt\" IS NULL OR \"BenefitsSnapshotRecordedAt\" IS NULL OR \"MaxActiveParcelsSnapshot\" IS NULL OR \"MaxActiveParcelsSnapshot\"<=0)",
        ["task_source"] = "SELECT count(*) FROM \"OperatorTaskHandover\" h LEFT JOIN \"Incident\" i ON h.\"TaskId\"=i.\"Id\" WHERE h.\"TaskKind\"='incident' AND (i.\"Id\" IS NULL OR i.\"LockerId\"<>h.\"LockerId\")",
        ["return_cancellation_owner"] = "SELECT count(*) FROM \"ReturnCancellationRequest\" c JOIN \"ReturnRequest\" r ON c.\"ReturnRequestId\"=r.\"Id\" JOIN \"ResidentProfile\" p ON r.\"ResidentProfileId\"=p.\"Id\" WHERE c.\"ResidentUserId\"<>p.\"UserId\"",
        ["maintenance_incident_scope"] = "SELECT count(*) FROM \"MaintenanceRequest\" m JOIN \"Incident\" i ON m.\"IncidentId\"=i.\"Id\" WHERE m.\"LockerId\"<>i.\"LockerId\" OR (m.\"Status\"='Closed' AND (m.\"ClosedAt\" IS NULL OR i.\"Status\"<>'Resolved'))"
    };
    checks.AddRange(businessChecks.Select(item => $"SELECT '{item.Key}' AS name, ({item.Value}) AS count"));
    await using var command = new NpgsqlCommand(string.Join(" UNION ALL ", checks),
        (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        if (reader.GetInt64(1) != 0) throw new Exception($"Data integrity check failed: {reader.GetString(0)}={reader.GetInt64(1)}");
    Console.WriteLine($"PASS: {checks.Count} foreign-key and business relationship checks.");
}
