using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.DeliveryRequests;
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
        var service = new DeliveryRequestService(dbContext, tokenService);

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
        var service = new DeliveryRequestService(dbContext, new Sha256TokenHashService());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InitiateAsync(new InitiateDeliveryRequest(locker.Code)));
    }

    [Fact]
    public async Task UploadImageAsync_WithHttpsUrl_StoresUrlAndRefreshesSession()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        var service = new DeliveryRequestService(dbContext, new Sha256TokenHashService());
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
        var service = new DeliveryRequestService(dbContext, new Sha256TokenHashService());
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
        var service = new DeliveryRequestService(dbContext, new Sha256TokenHashService());
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
        var service = new DeliveryRequestService(dbContext, new Sha256TokenHashService());
        InitiateDeliveryResponse initiated = await service.InitiateAsync(new InitiateDeliveryRequest(locker.Code));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitRecipientAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitRecipientPhoneRequest("0901234567")));
    }

    [Fact]
    public async Task SubmitRecipientAsync_WithAutoApprovalResident_SetsApproved()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0901234567");
        var service = new DeliveryRequestService(dbContext, new Sha256TokenHashService());
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
        Assert.Equal(DeliveryRequestStatus.Approved, response.Status);
        Assert.Equal(resident.Id, persisted.ResidentProfileId);
        Assert.Equal("0901234567", persisted.RecipientPhoneSnapshot);
        Assert.Equal(resident.DeliveryApprovalMode, persisted.ApprovalModeSnapshot);
        Assert.Null(persisted.ApprovalExpiresAt);
    }

    [Fact]
    public async Task SubmitRecipientAsync_WithManualApprovalResident_SetsPendingApprovalAndExpiry()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(dbContext);
        ResidentProfile resident = await SeedResidentAsync(dbContext, "0901234568", DeliveryApprovalMode.Manual);
        var service = new DeliveryRequestService(dbContext, new Sha256TokenHashService());
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
        Assert.NotNull(persisted.ApprovalExpiresAt);
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
        var service = new DeliveryRequestService(dbContext, new Sha256TokenHashService());

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
            FullName = "Resident",
            DeliveryApprovalMode = approvalMode,
            PersonalQrTokenHash = "hash",
            PersonalQrIssuedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.AddRange(user, profile);
        await dbContext.SaveChangesAsync();
        return profile;
    }

    private static DeliveryRequest CreateDeliveryRequest(
        Guid lockerId,
        Guid systemPolicyId,
        string tokenHash,
        DeliveryRequestStatus status,
        DateTimeOffset expiresAt)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            LockerId = lockerId,
            SystemPolicyId = systemPolicyId,
            GuestSessionTokenHash = tokenHash,
            Status = status,
            LastActivityAt = now.AddMinutes(-5),
            SessionExpiresAt = expiresAt,
            CreatedAt = now.AddMinutes(-5),
            UpdatedAt = now.AddMinutes(-5)
        };
    }
}
