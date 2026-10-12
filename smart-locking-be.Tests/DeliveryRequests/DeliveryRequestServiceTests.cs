using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.DTOs.Lockers;
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

        InitiateDeliveryResponse response = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

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
            () => service.CreateAsync(new InitiateDeliveryRequest(locker.Code)));
    }

    [Fact]
    public async Task SubmitAsync_WithHttpsUrl_StoresUrlAndRefreshesSession()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        await SeedResidentAsync(dbContext, "0901234567");
        DeliveryRequestService service = CreateService(dbContext);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));
        DateTimeOffset originalExpiry = initiated.SessionExpiresAt;

        DeliveryRequestSummaryResponse response = await service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("  https://cdn.example.com/parcel.webp  ", "0901234567"));

        Assert.Equal("https://cdn.example.com/parcel.webp", response.ParcelImageUrl);
        Assert.True(response.SessionExpiresAt >= originalExpiry);
    }

    [Fact]
    public async Task SubmitAsync_WithRelativeUrl_ThrowsArgumentException()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        DeliveryRequestService service = CreateService(dbContext);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("/images/parcel.jpg", "0901234567")));
    }

    [Fact]
    public async Task SubmitAsync_WithExpiredSession_PersistsExpirationAndThrowsTimeoutException()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        DeliveryRequestService service = CreateService(dbContext);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));
        DeliveryRequest deliveryRequest = await dbContext.DeliveryRequests.SingleAsync();
        deliveryRequest.SessionExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", "0901234567")));

        Assert.Equal(DeliveryRequestStatus.Expired, deliveryRequest.Status);
        Assert.Equal(DeliveryRequestFailureCode.SessionExpired, deliveryRequest.FailureCode);
    }

    [Fact]
    public async Task SubmitAsync_WithoutImage_ThrowsArgumentException()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        DeliveryRequestService service = CreateService(dbContext);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("", "0901234567")));
    }

    [Fact]
    public async Task SubmitRecipientAsync_WithAutoApprovalResident_ApprovesWithoutPush()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0901234567");
        var pushNotificationService = new RecordingPushNotificationService();
        DeliveryRequestService service = CreateService(dbContext, pushNotificationService: pushNotificationService);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));
        DeliveryRequestSummaryResponse response = await service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", " 0901234567 "));

        DeliveryRequest persisted = await dbContext.DeliveryRequests.SingleAsync();
        Assert.Equal(DeliveryRequestStatus.Approved, response.Status);
        Assert.Equal(resident.Id, persisted.ResidentProfileId);
        Assert.Equal("0901234567", persisted.RecipientPhoneSnapshot);
        Assert.Equal(DeliveryApprovalMode.Auto, persisted.ApprovalModeSnapshot);
        Assert.Equal(persisted.UpdatedAt, persisted.DecisionAt);
        Assert.Null(persisted.ApprovalExpiresAt);
        Assert.Empty(pushNotificationService.Requests);
    }

    [Fact]
    public async Task SubmitRecipientAsync_WithManualApprovalResident_SetsPendingApprovalAndExpiry()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0901234568", DeliveryApprovalMode.Manual);
        var pushNotificationService = new RecordingPushNotificationService();
        DeliveryRequestService service = CreateService(dbContext, pushNotificationService: pushNotificationService);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));
        DeliveryRequestSummaryResponse response = await service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", resident.User.PhoneNumber!));

        DeliveryRequest persisted = await dbContext.DeliveryRequests.SingleAsync();
        Assert.Equal(DeliveryRequestStatus.PendingApproval, response.Status);
        Assert.Equal(DeliveryApprovalMode.Manual, persisted.ApprovalModeSnapshot);
        Assert.Equal(TimeSpan.FromMinutes(10), persisted.ApprovalExpiresAt - persisted.UpdatedAt);
        Assert.Equal(
            [(resident.UserId, persisted.Id, locker.Code)],
            pushNotificationService.Requests);
    }

    [Fact]
    public async Task SubmitRecipientAsync_WhenResidentUsesAnotherLocker_RejectsRequest()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0901234570");
        resident.RegisteredLockerId = Guid.NewGuid();
        await dbContext.SaveChangesAsync();
        DeliveryRequestService service = CreateService(dbContext);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAsync(
                initiated.Id,
                initiated.GuestSessionToken,
                new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", resident.User.PhoneNumber!)));

        Assert.Contains("không đăng ký", exception.Message);
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
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));
        DeliveryRequestSummaryResponse response = await service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", resident.User.PhoneNumber!));

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

        CompartmentReservationResponse response = await service.OpenCompartmentAsync(request.Id, token);

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

        await service.FinalizeDropOffAsync(request.Id, DateTimeOffset.UtcNow);

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
    public async Task FinalizeDropOffAsync_DoesNotDependOnStaleDoorState()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(dbContext);
        LockerCompartment compartment = SeedCompartment(dbContext, locker.Id, 1);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0909999998");
        compartment.DoorStatus = DoorStatus.Open;
        string token = "valid-token";
        var tokenService = new Sha256TokenHashService();
        DeliveryRequest request = CreateDeliveryRequest(
            locker.Id, policy.Id, tokenService.HashToken(token), DeliveryRequestStatus.Allocated,
            DateTimeOffset.UtcNow.AddMinutes(10));
        request.ResidentProfileId = resident.Id;
        request.AllocatedCompartmentId = compartment.Id;
        request.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        dbContext.DeliveryRequests.Add(request);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, tokenService).FinalizeDropOffAsync(request.Id, DateTimeOffset.UtcNow);

        Assert.Single(dbContext.Parcels);
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

    #region DEL-01 to DEL-11 Matrix Tests

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        InitiateDeliveryResponse res = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        Assert.Equal(DeliveryRequestStatus.Started, res.Status);
        Assert.NotNull(res.GuestSessionToken);
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(new InitiateDeliveryRequest("NON_EXISTENT")));
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        locker.OperationalStatus = LockerOperationalStatus.OutOfService;
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new InitiateDeliveryRequest(locker.Code)));
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        locker.OperationalStatus = LockerOperationalStatus.Inactive;
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new InitiateDeliveryRequest(locker.Code)));
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new InitiateDeliveryRequest("")));
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        await SeedResidentAsync(db, "0911222333", DeliveryApprovalMode.Auto);
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        DeliveryRequestSummaryResponse res = await service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", "0911222333"));

        Assert.Equal(DeliveryRequestStatus.Approved, res.Status);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        await SeedResidentAsync(db, "0911222333");
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        DeliveryRequest req = await db.DeliveryRequests.SingleAsync(r => r.Id == initiated.Id);
        req.SessionExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", "0911222333")));
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        await SeedResidentAsync(db, "0911222333");
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => service.SubmitAsync(
            initiated.Id,
            "WRONG_TOKEN",
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", "0911222333")));
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        await SeedResidentAsync(db, "0911222333");
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("", "0911222333")));
    }

    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        GuestDeliveryStatusResponse res = await service.GetAsync(initiated.Id, initiated.GuestSessionToken);

        Assert.Equal(initiated.Id, res.RequestId);
    }

    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(Guid.NewGuid(), "TOKEN"));
    }

    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => service.GetAsync(initiated.Id, "WRONG_TOKEN"));
    }

    [Fact]
    [Trait("Category", "DEL-04")]
    public async Task ExpireStartedSessionsAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        db.DeliveryRequests.Add(CreateDeliveryRequest(locker.Id, policy.Id, "hash1", DeliveryRequestStatus.Started, DateTimeOffset.UtcNow.AddMinutes(-5)));
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpireStartedSessionsAsync();

        Assert.True(count >= 1);
    }

    [Fact]
    [Trait("Category", "DEL-04")]
    public async Task ExpireStartedSessionsAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        db.DeliveryRequests.Add(CreateDeliveryRequest(locker.Id, policy.Id, "hash1", DeliveryRequestStatus.Started, DateTimeOffset.UtcNow.AddMinutes(15)));
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpireStartedSessionsAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "DEL-04")]
    public async Task ExpireStartedSessionsAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpireStartedSessionsAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "DEL-05")]
    public async Task GetPendingRequestsForResidentAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0988776655");
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ResidentProfileId = res.Id;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        PagedResult<PendingDeliveryRequestResponse> result = await service.GetPendingRequestsForResidentAsync(res.UserId, default, 1, 10);

        Assert.Single(result.Items);
    }

    [Fact]
    [Trait("Category", "DEL-05")]
    public async Task GetPendingRequestsForResidentAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0988776655");
        DeliveryRequestService service = CreateService(db);

        PagedResult<PendingDeliveryRequestResponse> result = await service.GetPendingRequestsForResidentAsync(res.UserId, default, 1, 10);

        Assert.Empty(result.Items);
    }

    [Fact]
    [Trait("Category", "DEL-05")]
    public async Task GetPendingRequestsForResidentAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0988776655");
        DeliveryRequestService service = CreateService(db);

        PagedResult<PendingDeliveryRequestResponse> result = await service.GetPendingRequestsForResidentAsync(res.UserId, default, 1, 20);

        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0912345678");
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ResidentProfileId = res.Id;
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        DeliveryRequestSummaryResponse response = await service.ApproveDeliveryRequestAsync(res.UserId, req.Id);

        Assert.Equal(DeliveryRequestStatus.Approved, response.Status);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0912345678");
        ResidentProfile otherRes = await SeedResidentAsync(db, "0999888777");
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ResidentProfileId = res.Id;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveDeliveryRequestAsync(otherRes.UserId, req.Id));
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0912345678");
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.Started, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ResidentProfileId = res.Id;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveDeliveryRequestAsync(res.UserId, req.Id));
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0912345678");
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ResidentProfileId = res.Id;
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<TimeoutException>(() => service.ApproveDeliveryRequestAsync(res.UserId, req.Id));
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0912345678");
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ApproveDeliveryRequestAsync(res.UserId, Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ApproveDeliveryRequestAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0912345678");
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ResidentProfileId = res.Id;
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        DeliveryRequestSummaryResponse response = await service.RejectDeliveryRequestAsync(res.UserId, req.Id);

        Assert.Equal(DeliveryRequestStatus.Rejected, response.Status);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0912345678");
        ResidentProfile otherRes = await SeedResidentAsync(db, "0999888777");
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ResidentProfileId = res.Id;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RejectDeliveryRequestAsync(otherRes.UserId, req.Id));
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0912345678");
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.Started, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ResidentProfileId = res.Id;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RejectDeliveryRequestAsync(res.UserId, req.Id));
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0912345678");
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ResidentProfileId = res.Id;
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<TimeoutException>(() => service.RejectDeliveryRequestAsync(res.UserId, req.Id));
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0912345678");
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RejectDeliveryRequestAsync(res.UserId, Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RejectDeliveryRequestAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "DEL-08")]
    public async Task ExpirePendingApprovalsAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpirePendingApprovalsAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    [Trait("Category", "DEL-08")]
    public async Task ExpirePendingApprovalsAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpirePendingApprovalsAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "DEL-08")]
    public async Task ExpirePendingApprovalsAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpirePendingApprovalsAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        SeedCompartment(db, locker.Id, 1);
        var hashService = new Sha256TokenHashService();
        string rawToken = "SECRET_TOKEN_01";
        var req = CreateDeliveryRequest(locker.Id, policy.Id, hashService.HashToken(rawToken), DeliveryRequestStatus.Approved, DateTimeOffset.UtcNow.AddMinutes(5));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db, hashService);

        CompartmentReservationResponse res = await service.OpenCompartmentAsync(req.Id, rawToken);

        Assert.NotNull(res.CompartmentCode);
        Assert.Equal(DeliveryRequestStatus.Allocated, req.Status);
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        SeedCompartment(db, locker.Id, 1);
        var hashService = new Sha256TokenHashService();
        var req = CreateDeliveryRequest(locker.Id, policy.Id, hashService.HashToken("TOKEN"), DeliveryRequestStatus.Approved, DateTimeOffset.UtcNow.AddMinutes(5));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db, hashService);

        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => service.OpenCompartmentAsync(req.Id, "WRONG_TOKEN"));
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        SeedCompartment(db, locker.Id, 1);
        var hashService = new Sha256TokenHashService();
        string rawToken = "RAW";
        var req = CreateDeliveryRequest(locker.Id, policy.Id, hashService.HashToken(rawToken), DeliveryRequestStatus.Started, DateTimeOffset.UtcNow.AddMinutes(5));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db, hashService);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(req.Id, rawToken));
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var hashService = new Sha256TokenHashService();
        string rawToken = "RAW";
        var req = CreateDeliveryRequest(locker.Id, policy.Id, hashService.HashToken(rawToken), DeliveryRequestStatus.Approved, DateTimeOffset.UtcNow.AddMinutes(5));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db, hashService);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(req.Id, rawToken));
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.OpenCompartmentAsync(Guid.NewGuid(), "TOKEN"));
    }

    [Fact]
    [Trait("Category", "DEL-10")]
    public async Task ExpireReservationsAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var comp = SeedCompartment(db, locker.Id, 1);
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(5));
        req.AllocatedCompartmentId = comp.Id;
        req.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpireReservationsAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    [Trait("Category", "DEL-10")]
    public async Task ExpireReservationsAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var comp = SeedCompartment(db, locker.Id, 1);
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(5));
        req.AllocatedCompartmentId = comp.Id;
        req.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpireReservationsAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "DEL-10")]
    public async Task ExpireReservationsAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpireReservationsAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile res = await SeedResidentAsync(db, "0911223344");
        var comp = SeedCompartment(db, locker.Id, 1);
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(5));
        req.ResidentProfileId = res.Id;
        req.AllocatedCompartmentId = comp.Id;
        req.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await service.FinalizeDropOffAsync(req.Id, DateTimeOffset.UtcNow);

        Assert.Equal(DeliveryRequestStatus.Deposited, req.Status);
        Assert.Single(db.Parcels.Where(p => p.DeliveryRequestId == req.Id));
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var comp = SeedCompartment(db, locker.Id, 1);
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(5));
        req.AllocatedCompartmentId = comp.Id;
        req.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5); // expired
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<TimeoutException>(() => service.FinalizeDropOffAsync(req.Id, DateTimeOffset.UtcNow));
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var comp = SeedCompartment(db, locker.Id, 1);
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.Started, DateTimeOffset.UtcNow.AddMinutes(5));
        req.AllocatedCompartmentId = comp.Id;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinalizeDropOffAsync(req.Id, DateTimeOffset.UtcNow));
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.FinalizeDropOffAsync(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(5));
        // No allocated compartment id
        req.AllocatedCompartmentId = null;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinalizeDropOffAsync(req.Id, DateTimeOffset.UtcNow));
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var comp = SeedCompartment(db, locker.Id, 1);
        var req = CreateDeliveryRequest(locker.Id, policy.Id, "hash", DeliveryRequestStatus.Deposited, DateTimeOffset.UtcNow.AddMinutes(5));
        req.AllocatedCompartmentId = comp.Id;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        // Deposited already -> idempotent return
        await service.FinalizeDropOffAsync(req.Id, DateTimeOffset.UtcNow);
        Assert.Equal(DeliveryRequestStatus.Deposited, req.Status);
    }

    #endregion

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
            new SuccessfulLockerAccessService(),
            new CompartmentAllocationService(dbContext, timeProvider ?? TimeProvider.System),
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
            ManualApprovalTimeoutMinutes = 10,
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
        Guid registeredLockerId = await dbContext.Lockers.Select(locker => locker.Id).SingleAsync();
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
            RegisteredLockerId = registeredLockerId,
            FullName = "Cư Dân",
            DeliveryApprovalMode = approvalMode,
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

        public Guid EnqueueParcelStored(
            Guid residentUserId,
            Guid deliveryRequestId,
            Guid parcelId,
            string lockerCode,
            string compartmentCode) => Guid.NewGuid();

        public Guid EnqueueReturnNotification(
            Guid residentUserId,
            Guid returnRequestId,
            string type,
            string title,
            string message) => Guid.NewGuid();

        public Guid EnqueueParcelTransferred(
            Guid residentUserId,
            Guid deliveryRequestId,
            Guid parcelId,
            string collectionAddress) => Guid.NewGuid();

        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue) => Guid.NewGuid();

        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class SuccessfulLockerAccessService : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(
            OpenLockerRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new OpenLockerResponse(
                Guid.NewGuid(),
                request.LockerId,
                request.LockerCompartmentId,
                "simulator",
                1,
                LockerAccessResult.Succeeded,
                null,
                DateTimeOffset.UtcNow));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
