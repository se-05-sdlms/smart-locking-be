using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.DeliveryRequests;

public sealed class DeliveryRequestServiceTests
{
    [Fact]
    public async Task InitiateAsync_WithOperationalLocker_CreatesStartedSessionAndReturnsRawToken()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(dbContext);
        var tokenService = new Sha256TokenHashService();
        DeliveryRequestService service = CreateService(dbContext, tokenService);

        InitiateDeliveryResponse response = await service.InitiateAsync(new InitiateDeliveryRequest(locker.Code));

        DeliveryRequest persisted = await dbContext.DeliveryRequests.SingleAsync();
        Assert.Equal(DeliveryRequestStatus.Started, response.Status);
        Assert.Equal(locker.Id, persisted.LockerId);
        Assert.Equal(policy.Id, persisted.SystemPolicyId);
        Assert.Equal(tokenService.HashToken(response.GuestSessionToken), persisted.GuestSessionTokenHash);
        Assert.True(response.SessionExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task InitiateAsync_WithUnavailableLocker_ThrowsInvalidOperationException()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        locker.OperationalStatus = LockerOperationalStatus.OutOfService;
        await dbContext.SaveChangesAsync();
        DeliveryRequestService service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InitiateAsync(new InitiateDeliveryRequest(locker.Code)));
    }

    [Fact]
    public async Task UploadImageAsync_WithHttpsUrl_StoresUrlAndRefreshesSession()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        DeliveryRequestService service = CreateService(dbContext);
        InitiateDeliveryResponse initiated = await service.InitiateAsync(new InitiateDeliveryRequest(locker.Code));
        DateTimeOffset originalExpiry = initiated.SessionExpiresAt;

        DeliveryRequestSummaryResponse response = await service.UploadImageAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new UploadParcelImageRequest("  https://cdn.example.com/parcel.webp  "));

        Assert.Equal("https://cdn.example.com/parcel.webp", response.ParcelImageUrl);
        Assert.True(response.SessionExpiresAt >= originalExpiry);
    }

    [Fact]
    public async Task UploadImageAsync_WithRelativeUrl_ThrowsArgumentException()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        DeliveryRequestService service = CreateService(dbContext);
        InitiateDeliveryResponse initiated = await service.InitiateAsync(new InitiateDeliveryRequest(locker.Code));

        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadImageAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new UploadParcelImageRequest("/images/parcel.jpg")));
    }

    [Fact]
    public async Task UploadImageAsync_WithExpiredSession_PersistsExpirationAndThrowsTimeoutException()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        DeliveryRequestService service = CreateService(dbContext);
        InitiateDeliveryResponse initiated = await service.InitiateAsync(new InitiateDeliveryRequest(locker.Code));
        DeliveryRequest deliveryRequest = await dbContext.DeliveryRequests.SingleAsync();
        deliveryRequest.SessionExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => service.UploadImageAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new UploadParcelImageRequest("https://cdn.example.com/parcel.jpg")));

        Assert.Equal(DeliveryRequestStatus.Expired, deliveryRequest.Status);
        Assert.Equal(DeliveryRequestFailureCode.SessionExpired, deliveryRequest.FailureCode);
    }

    [Fact]
    public async Task SubmitRecipientAsync_WithoutImage_ThrowsInvalidOperationException()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        DeliveryRequestService service = CreateService(dbContext);
        InitiateDeliveryResponse initiated = await service.InitiateAsync(new InitiateDeliveryRequest(locker.Code));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitRecipientAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitRecipientPhoneRequest("0901234567")));
    }

    [Fact]
    public async Task SubmitRecipientAsync_WithAutoApprovalResident_StillRequiresManualApprovalForTenMinutes()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0901234567");
        var pushNotificationService = new RecordingPushNotificationService();
        DeliveryRequestService service = CreateService(dbContext, pushNotificationService: pushNotificationService);
        InitiateDeliveryResponse initiated = await service.InitiateAsync(new InitiateDeliveryRequest(locker.Code));
        await service.UploadImageAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new UploadParcelImageRequest("https://cdn.example.com/parcel.jpg"));

        DeliveryRequestSummaryResponse response = await service.SubmitRecipientAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitRecipientPhoneRequest(" 0901234567 "));

        DeliveryRequest persisted = await dbContext.DeliveryRequests.SingleAsync();
        Assert.Equal(DeliveryRequestStatus.PendingApproval, response.Status);
        Assert.Equal(resident.Id, persisted.ResidentProfileId);
        Assert.Equal("0901234567", persisted.RecipientPhoneSnapshot);
        Assert.Equal(DeliveryApprovalMode.Manual, persisted.ApprovalModeSnapshot);
        Assert.Equal(TimeSpan.FromMinutes(10), persisted.ApprovalExpiresAt - persisted.UpdatedAt);
        Assert.Equal(
            [(resident.UserId, persisted.Id, locker.Code)],
            pushNotificationService.Requests);
    }

    [Fact]
    public async Task SubmitRecipientAsync_WithManualApprovalResident_SetsPendingApprovalAndExpiry()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0901234568", DeliveryApprovalMode.Manual);
        var pushNotificationService = new RecordingPushNotificationService();
        DeliveryRequestService service = CreateService(dbContext, pushNotificationService: pushNotificationService);
        InitiateDeliveryResponse initiated = await service.InitiateAsync(new InitiateDeliveryRequest(locker.Code));
        await service.UploadImageAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new UploadParcelImageRequest("https://cdn.example.com/parcel.jpg"));

        DeliveryRequestSummaryResponse response = await service.SubmitRecipientAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitRecipientPhoneRequest(resident.User.PhoneNumber!));

        DeliveryRequest persisted = await dbContext.DeliveryRequests.SingleAsync();
        Assert.Equal(DeliveryRequestStatus.PendingApproval, response.Status);
        Assert.Equal(DeliveryApprovalMode.Manual, persisted.ApprovalModeSnapshot);
        Assert.Equal(TimeSpan.FromMinutes(10), persisted.ApprovalExpiresAt - persisted.UpdatedAt);
        Assert.Equal(
            [(resident.UserId, persisted.Id, locker.Code)],
            pushNotificationService.Requests);
    }

    [Fact]
    public async Task SubmitRecipientAsync_WhenPushCannotBeDelivered_KeepsRequestAndInAppNotification()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        ResidentProfile resident = await SeedResidentAsync(
            dbContext,
            "0901234569",
            DeliveryApprovalMode.Manual);
        var notificationService = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);
        DeliveryRequestService service = CreateService(
            dbContext,
            pushNotificationService: notificationService);
        InitiateDeliveryResponse initiated = await service.InitiateAsync(new InitiateDeliveryRequest(locker.Code));
        await service.UploadImageAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new UploadParcelImageRequest("https://cdn.example.com/parcel.jpg"));

        DeliveryRequestSummaryResponse response = await service.SubmitRecipientAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitRecipientPhoneRequest(resident.User.PhoneNumber!));

        Assert.Equal(DeliveryRequestStatus.PendingApproval, response.Status);
        Assert.Equal(DeliveryRequestStatus.PendingApproval, (await dbContext.DeliveryRequests.SingleAsync()).Status);
        Assert.Contains(
            dbContext.Notifications,
            notification =>
                notification.Channel == NotificationChannel.InApp &&
                notification.DeliveryStatus == NotificationDeliveryStatus.Sent);
        Assert.Contains(
            dbContext.Notifications,
            notification =>
                notification.Channel == NotificationChannel.Push &&
                notification.DeliveryStatus == NotificationDeliveryStatus.Failed);
    }

    [Fact]
    public async Task ApproveDeliveryRequestAsync_WhenPending_SetsApprovedAndDecisionAt()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(dbContext);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0901234599", DeliveryApprovalMode.Manual);
        DeliveryRequest request = CreateDeliveryRequest(locker.Id, policy.Id, "token", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        request.ResidentProfileId = resident.Id;
        dbContext.DeliveryRequests.Add(request);
        await dbContext.SaveChangesAsync();
        DeliveryRequestService service = CreateService(dbContext);

        DeliveryRequestSummaryResponse response = await service.ApproveDeliveryRequestAsync(resident.UserId, request.Id);

        Assert.Equal(DeliveryRequestStatus.Approved, response.Status);
        Assert.NotNull(request.DecisionAt);
    }

    [Fact]
    public async Task RejectDeliveryRequestAsync_WhenPending_SetsRejectedAndDecisionAt()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(dbContext);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0901234598", DeliveryApprovalMode.Manual);
        DeliveryRequest request = CreateDeliveryRequest(locker.Id, policy.Id, "token", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        request.ResidentProfileId = resident.Id;
        dbContext.DeliveryRequests.Add(request);
        await dbContext.SaveChangesAsync();
        DeliveryRequestService service = CreateService(dbContext);

        DeliveryRequestSummaryResponse response = await service.RejectDeliveryRequestAsync(resident.UserId, request.Id);

        Assert.Equal(DeliveryRequestStatus.Rejected, response.Status);
        Assert.NotNull(request.DecisionAt);
    }

    [Fact]
    public async Task ApproveDeliveryRequestAsync_AtTenMinuteDeadline_ExpiresRequest()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(dbContext);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0901234597", DeliveryApprovalMode.Manual);
        DateTimeOffset deadline = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
        DeliveryRequest request = CreateDeliveryRequest(
            locker.Id,
            policy.Id,
            "token",
            DeliveryRequestStatus.PendingApproval,
            deadline.AddMinutes(5));
        request.ResidentProfileId = resident.Id;
        request.ApprovalExpiresAt = deadline;
        dbContext.DeliveryRequests.Add(request);
        await dbContext.SaveChangesAsync();
        DeliveryRequestService service = CreateService(
            dbContext,
            timeProvider: new FixedTimeProvider(deadline));

        await Assert.ThrowsAsync<TimeoutException>(
            () => service.ApproveDeliveryRequestAsync(resident.UserId, request.Id));

        Assert.Equal(DeliveryRequestStatus.Expired, request.Status);
        Assert.Equal(DeliveryRequestFailureCode.ApprovalExpired, request.FailureCode);
    }

    [Fact]
    public async Task ReserveCompartmentAsync_WithAvailableCompartment_AllocatesCompartmentAndCreatesReservation()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(dbContext);
        LockerCompartment compartment = SeedCompartment(dbContext, locker.Id, 1);
        string token = "valid-token";
        var tokenService = new Sha256TokenHashService();
        DeliveryRequest request = CreateDeliveryRequest(locker.Id, policy.Id, tokenService.HashToken(token), DeliveryRequestStatus.Approved, DateTimeOffset.UtcNow.AddMinutes(10));
        dbContext.DeliveryRequests.Add(request);
        await dbContext.SaveChangesAsync();
        DeliveryRequestService service = CreateService(dbContext, tokenService);

        CompartmentReservationResponse response = await service.ReserveCompartmentAsync(request.Id, token);

        Assert.Equal(request.Id, response.RequestId);
        Assert.Equal(compartment.Id, response.CompartmentId);
        Assert.Equal(compartment.Code, response.CompartmentCode);
        Assert.Equal(DeliveryRequestStatus.Allocated, request.Status);
        Assert.Equal(compartment.Id, request.AllocatedCompartmentId);
        Assert.Single(dbContext.CompartmentReservations);
    }

    [Fact]
    public async Task ConfirmDropOffAsync_WhenAllocated_CreatesParcelAndSetsDeposited()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(dbContext);
        LockerCompartment compartment = SeedCompartment(dbContext, locker.Id, 1);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0909999999");
        string token = "valid-token";
        var tokenService = new Sha256TokenHashService();
        DeliveryRequest request = CreateDeliveryRequest(locker.Id, policy.Id, tokenService.HashToken(token), DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(10));
        request.ResidentProfileId = resident.Id;
        request.AllocatedCompartmentId = compartment.Id;
        request.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        request.ParcelImageUrl = "https://cdn.example.com/parcel.jpg";
        dbContext.DeliveryRequests.Add(request);

        CompartmentReservation reservation = new()
        {
            Id = Guid.NewGuid(),
            LockerCompartmentId = compartment.Id,
            DeliveryRequestId = request.Id,
            ReservedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.CompartmentReservations.Add(reservation);
        await dbContext.SaveChangesAsync();
        DeliveryRequestService service = CreateService(dbContext, tokenService);

        DropOffConfirmationResponse response = await service.ConfirmDropOffAsync(request.Id, token);

        Assert.Equal(DeliveryRequestStatus.Deposited, response.Status);
        Assert.Equal(DeliveryRequestStatus.Deposited, request.Status);
        Assert.NotNull(reservation.ReleasedAt);

        Parcel parcel = await dbContext.Parcels.SingleAsync();
        Assert.Equal(ParcelStatus.Stored, parcel.Status);
        Assert.Equal(request.Id, parcel.DeliveryRequestId);

        ParcelStatusHistory history = await dbContext.ParcelStatusHistories.SingleAsync();
        Assert.Equal(parcel.Id, history.ParcelId);
        Assert.Equal(ParcelStatus.Stored, history.ToStatus);
    }

    [Fact]
    public async Task ExpireStartedSessionsAsync_ExpiresOnlyStartedRequests()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(dbContext);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeliveryRequest started = CreateDeliveryRequest(locker.Id, policy.Id, "started", DeliveryRequestStatus.Started, now.AddMinutes(-1));
        DeliveryRequest pending = CreateDeliveryRequest(locker.Id, policy.Id, "pending", DeliveryRequestStatus.PendingApproval, now.AddMinutes(-1));
        dbContext.DeliveryRequests.AddRange(started, pending);
        await dbContext.SaveChangesAsync();
        DeliveryRequestService service = CreateService(dbContext);

        int count = await service.ExpireStartedSessionsAsync();

        Assert.Equal(1, count);
        Assert.Equal(DeliveryRequestStatus.Expired, started.Status);
        Assert.Equal(DeliveryRequestFailureCode.SessionExpired, started.FailureCode);
        Assert.Equal(DeliveryRequestStatus.PendingApproval, pending.Status);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DeliveryRequestService CreateService(
        ApplicationDbContext dbContext,
        Sha256TokenHashService? tokenHashService = null,
        IPushNotificationService? pushNotificationService = null,
        TimeProvider? timeProvider = null) =>
        new(
            dbContext,
            tokenHashService ?? new Sha256TokenHashService(),
            pushNotificationService ?? new RecordingPushNotificationService(),
            timeProvider ?? TimeProvider.System);

    private static async Task<(Locker Locker, SystemPolicy Policy)> SeedLockerAndPolicyAsync(ApplicationDbContext dbContext)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        User administrator = new()
        {
            Id = Guid.NewGuid(),
            Email = "admin@example.com",
            PasswordHash = "hash",
            Role = UserRole.Administrator,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        Locker locker = new()
        {
            Id = Guid.NewGuid(),
            Code = "LOCKER-01",
            Address = "Address",
            RecoveryAddress = "Recovery address",
            DeviceIdentifier = "device-01",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = now,
            UpdatedAt = now
        };
        SystemPolicy policy = new()
        {
            Id = Guid.NewGuid(),
            Version = 1,
            DefaultApprovalMode = DeliveryApprovalMode.Manual,
            GuestSessionTimeoutMinutes = 15,
            ManualApprovalTimeoutMinutes = 30,
            CompartmentReservationMinutes = 10,
            OverdueStartAfterHours = 24,
            Currency = "VND",
            MaxStorageHours = 72,
            OtpMaxAttempts = 5,
            OtpLockoutMinutes = 15,
            EffectiveFrom = now.AddMinutes(-1),
            IsActive = true,
            CreatedByUserId = administrator.Id,
            CreatedAt = now
        };

        dbContext.AddRange(administrator, locker, policy);
        await dbContext.SaveChangesAsync();
        return (locker, policy);
    }

    private static async Task<ResidentProfile> SeedResidentAsync(
        ApplicationDbContext dbContext,
        string phoneNumber,
        DeliveryApprovalMode approvalMode = DeliveryApprovalMode.Auto)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        User user = new()
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber,
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        ResidentProfile profile = new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FullName = "Cư Dân",
            DeliveryApprovalMode = approvalMode,
            PersonalQrTokenHash = "qr-hash",
            PersonalQrIssuedAt = now,
            User = user,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.AddRange(user, profile);
        await dbContext.SaveChangesAsync();
        return profile;
    }

    private static LockerCompartment SeedCompartment(ApplicationDbContext dbContext, Guid lockerId, int number)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LockerCompartment compartment = new()
        {
            Id = Guid.NewGuid(),
            LockerId = lockerId,
            Code = $"C{number:D2}",
            HardwareCode = $"HW-C{number:D2}",
            HardwareChannel = number,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Closed,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.LockerCompartments.Add(compartment);
        dbContext.SaveChanges();
        return compartment;
    }

    private static DeliveryRequest CreateDeliveryRequest(
        Guid lockerId,
        Guid policyId,
        string tokenHash,
        DeliveryRequestStatus status,
        DateTimeOffset sessionExpiresAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            LockerId = lockerId,
            SystemPolicyId = policyId,
            GuestSessionTokenHash = tokenHash,
            Status = status,
            LastActivityAt = DateTimeOffset.UtcNow,
            SessionExpiresAt = sessionExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private sealed class RecordingPushNotificationService : IPushNotificationService
    {
        public List<(Guid UserId, Guid RequestId, string LockerCode)> Requests { get; } = [];

        public Guid EnqueueDeliveryApprovalRequest(
            Guid residentUserId,
            Guid deliveryRequestId,
            string lockerCode)
        {
            Requests.Add((residentUserId, deliveryRequestId, lockerCode));
            return Guid.NewGuid();
        }

        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
