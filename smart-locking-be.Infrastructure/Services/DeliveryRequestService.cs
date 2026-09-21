using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Auth;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class DeliveryRequestService(
    ApplicationDbContext dbContext,
    ITokenHashService tokenHashService) : IDeliveryRequestService
{
    public async Task<InitiateDeliveryResponse> InitiateAsync(
        InitiateDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        string lockerCode = RequireValue(request.LockerCode, nameof(request.LockerCode), 50);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Locker locker = await dbContext.Lockers.SingleOrDefaultAsync(
            item => item.Code == lockerCode,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Locker với mã '{lockerCode}' không tồn tại.");

        if (locker.OperationalStatus != LockerOperationalStatus.Operational)
        {
            throw new InvalidOperationException("Locker hiện không sẵn sàng nhận hàng.");
        }

        SystemPolicy policy = await dbContext.SystemPolicies.SingleOrDefaultAsync(
            item => item.IsActive &&
                    item.EffectiveFrom <= now &&
                    (item.EffectiveTo == null || item.EffectiveTo > now),
            cancellationToken)
            ?? throw new InvalidOperationException("Không có chính sách hệ thống đang hiệu lực.");

        string guestSessionToken = tokenHashService.CreateSecureToken();
        DeliveryRequest deliveryRequest = new()
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            SystemPolicyId = policy.Id,
            GuestSessionTokenHash = tokenHashService.HashToken(guestSessionToken),
            Status = DeliveryRequestStatus.Started,
            LastActivityAt = now,
            SessionExpiresAt = now.AddMinutes(policy.GuestSessionTimeoutMinutes),
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.DeliveryRequests.Add(deliveryRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new InitiateDeliveryResponse(
            deliveryRequest.Id,
            guestSessionToken,
            deliveryRequest.Status,
            deliveryRequest.SessionExpiresAt);
    }

    public async Task<DeliveryRequestSummaryResponse> UploadImageAsync(
        Guid id,
        string guestSessionToken,
        UploadParcelImageRequest request,
        CancellationToken cancellationToken = default)
    {
        DeliveryRequest deliveryRequest = await FindStartedSessionAsync(id, guestSessionToken, cancellationToken);
        string parcelImageUrl = ValidateImageUrl(request.ParcelImageUrl);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        deliveryRequest.ParcelImageUrl = parcelImageUrl;
        RefreshSession(deliveryRequest, now);

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapSummary(deliveryRequest);
    }

    public async Task<DeliveryRequestSummaryResponse> SubmitRecipientAsync(
        Guid id,
        string guestSessionToken,
        SubmitRecipientPhoneRequest request,
        CancellationToken cancellationToken = default)
    {
        DeliveryRequest deliveryRequest = await FindStartedSessionAsync(id, guestSessionToken, cancellationToken);
        string recipientPhone = RequireValue(request.RecipientPhone, nameof(request.RecipientPhone), 20);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (deliveryRequest.ParcelImageUrl is null)
        {
            throw new InvalidOperationException("Phải cung cấp URL ảnh bưu kiện trước khi gửi thông tin người nhận.");
        }

        ResidentProfile resident = await dbContext.ResidentProfiles
            .Include(profile => profile.User)
            .SingleOrDefaultAsync(
                profile => profile.User.PhoneNumber == recipientPhone &&
                           profile.User.Role == UserRole.Resident &&
                           profile.User.Status == UserStatus.Active,
                cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy Cư dân đang hoạt động với số điện thoại đã nhập.");

        deliveryRequest.ResidentProfileId = resident.Id;
        deliveryRequest.RecipientPhoneSnapshot = recipientPhone;
        deliveryRequest.ApprovalModeSnapshot = resident.DeliveryApprovalMode;
        deliveryRequest.Status = resident.DeliveryApprovalMode == DeliveryApprovalMode.Auto
            ? DeliveryRequestStatus.Approved
            : DeliveryRequestStatus.PendingApproval;
        deliveryRequest.ApprovalExpiresAt = resident.DeliveryApprovalMode == DeliveryApprovalMode.Manual
            ? now.AddMinutes(deliveryRequest.SystemPolicy.ManualApprovalTimeoutMinutes)
            : null;
        deliveryRequest.LastActivityAt = now;
        deliveryRequest.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapSummary(deliveryRequest);
    }

    public async Task<int> ExpireStartedSessionsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<DeliveryRequest> expiredSessions = await dbContext.DeliveryRequests
            .Where(request =>
                request.Status == DeliveryRequestStatus.Started &&
                request.SessionExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (DeliveryRequest request in expiredSessions)
        {
            request.Status = DeliveryRequestStatus.Expired;
            request.FailureCode = DeliveryRequestFailureCode.SessionExpired;
            request.UpdatedAt = now;
        }

        if (expiredSessions.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return expiredSessions.Count;
    }

    private async Task<DeliveryRequest> FindStartedSessionAsync(
        Guid id,
        string guestSessionToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(guestSessionToken))
        {
            throw new UnauthorizedAccessException("Thiếu X-Guest-Session-Token.");
        }

        string tokenHash = tokenHashService.HashToken(guestSessionToken.Trim());
        DeliveryRequest deliveryRequest = await dbContext.DeliveryRequests
            .Include(request => request.SystemPolicy)
            .SingleOrDefaultAsync(request => request.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu giao hàng.");

        if (!string.Equals(deliveryRequest.GuestSessionTokenHash, tokenHash, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Guest session token không hợp lệ.");
        }

        if (deliveryRequest.Status != DeliveryRequestStatus.Started)
        {
            throw new InvalidOperationException($"Không thể cập nhật yêu cầu ở trạng thái {deliveryRequest.Status}.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (deliveryRequest.SessionExpiresAt <= now)
        {
            deliveryRequest.Status = DeliveryRequestStatus.Expired;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.SessionExpired;
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new TimeoutException("Phiên gửi hàng đã hết hạn.");
        }

        return deliveryRequest;
    }

    private static void RefreshSession(DeliveryRequest request, DateTimeOffset now)
    {
        request.LastActivityAt = now;
        request.SessionExpiresAt = now.AddMinutes(request.SystemPolicy.GuestSessionTimeoutMinutes);
        request.UpdatedAt = now;
    }

    private static string ValidateImageUrl(string value)
    {
        string url = RequireValue(value, nameof(value), 2048);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("ParcelImageUrl phải là URL HTTP hoặc HTTPS tuyệt đối.", nameof(value));
        }

        return url;
    }

    private static string RequireValue(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Giá trị không được để trống.", parameterName);
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"Giá trị không được vượt quá {maxLength} ký tự.", parameterName);
        }

        return trimmed;
    }

    private static DeliveryRequestSummaryResponse MapSummary(DeliveryRequest request) =>
        new(request.Id, request.Status, request.ParcelImageUrl, request.SessionExpiresAt);
}
