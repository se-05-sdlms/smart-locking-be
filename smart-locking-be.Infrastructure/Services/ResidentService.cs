using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ResidentService(
    ApplicationDbContext dbContext,
    ITokenHashService tokenHashService) : IResidentService
{
    public async Task<ResidentProfileResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        User user = await FindUserWithProfileAsync(userId, cancellationToken);
        ValidateUserAccess(user);

        ResidentProfile profile = user.ResidentProfile ?? await EnsureProfileCreatedAsync(user, cancellationToken);

        return MapToResponse(user, profile);
    }

    public async Task<ResidentProfileResponse> UpdateProfileAsync(
        Guid userId,
        UpdateResidentProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ArgumentException("Họ và tên không được để trống.", nameof(request.FullName));
        }

        string trimmedFullName = request.FullName.Trim();
        if (trimmedFullName.Length > 150)
        {
            throw new ArgumentException("Họ và tên không được vượt quá 150 ký tự.", nameof(request.FullName));
        }

        if (request.DateOfBirth.HasValue && request.DateOfBirth.Value > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new ArgumentException("Ngày sinh không được ở trong tương lai.", nameof(request.DateOfBirth));
        }

        if (!string.IsNullOrWhiteSpace(request.AvatarUrl) && request.AvatarUrl.Trim().Length > 2048)
        {
            throw new ArgumentException("Đường dẫn ảnh đại diện không được vượt quá 2048 ký tự.", nameof(request.AvatarUrl));
        }

        User user = await FindUserWithProfileAsync(userId, cancellationToken);
        ValidateUserAccess(user);

        ResidentProfile profile = user.ResidentProfile ?? await EnsureProfileCreatedAsync(user, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        profile.FullName = trimmedFullName;
        profile.DateOfBirth = request.DateOfBirth;
        profile.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        profile.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToResponse(user, profile);
    }

    public async Task<ResidentProfileResponse> UpdateApprovalModeAsync(
        Guid userId,
        UpdateApprovalModeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(DeliveryApprovalMode), request.DeliveryApprovalMode))
        {
            throw new ArgumentException("Chế độ phê duyệt không hợp lệ.", nameof(request.DeliveryApprovalMode));
        }

        User user = await FindUserWithProfileAsync(userId, cancellationToken);
        ValidateUserAccess(user);

        ResidentProfile profile = user.ResidentProfile ?? await EnsureProfileCreatedAsync(user, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        profile.DeliveryApprovalMode = request.DeliveryApprovalMode;
        profile.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToResponse(user, profile);
    }

    public async Task<PersonalQrResponse> GetPersonalQrAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        User user = await FindUserWithProfileAsync(userId, cancellationToken);
        ValidateUserAccess(user);

        ResidentProfile profile = user.ResidentProfile ?? await EnsureProfileCreatedAsync(user, cancellationToken);

        string rawToken = tokenHashService.CreateSecureToken();
        string tokenHash = tokenHashService.HashToken(rawToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        profile.PersonalQrTokenHash = tokenHash;
        profile.PersonalQrIssuedAt = now;
        profile.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new PersonalQrResponse(rawToken, now);
    }

    private async Task<User> FindUserWithProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .Include(u => u.ResidentProfile)
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản.");
    }

    private static void ValidateUserAccess(User user)
    {
        if (user.Status == UserStatus.Locked)
        {
            throw new InvalidOperationException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ Quản trị viên.");
        }

        if (user.Status == UserStatus.Disabled)
        {
            throw new InvalidOperationException("Tài khoản của bạn đã bị vô hiệu hóa.");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new InvalidOperationException("Tài khoản của bạn hiện không thể thực hiện thao tác.");
        }

        if (user.Role != UserRole.Resident)
        {
            throw new InvalidOperationException("Người dùng không mang vai trò Cư dân.");
        }
    }

    private async Task<ResidentProfile> EnsureProfileCreatedAsync(User user, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string initialRawToken = tokenHashService.CreateSecureToken();
        string initialTokenHash = tokenHashService.HashToken(initialRawToken);

        string defaultFullName = !string.IsNullOrWhiteSpace(user.PhoneNumber)
            ? user.PhoneNumber
            : (!string.IsNullOrWhiteSpace(user.Email) ? user.Email : "Cư dân");

        ResidentProfile profile = new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FullName = defaultFullName,
            DeliveryApprovalMode = DeliveryApprovalMode.Auto,
            PersonalQrTokenHash = initialTokenHash,
            PersonalQrIssuedAt = now,
            FaceRecognitionEnabled = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ResidentProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
        user.ResidentProfile = profile;

        return profile;
    }

    private static ResidentProfileResponse MapToResponse(User user, ResidentProfile profile) =>
        new(
            profile.Id,
            user.Id,
            profile.FullName,
            user.PhoneNumber,
            user.Email,
            profile.DateOfBirth,
            profile.AvatarUrl,
            profile.DeliveryApprovalMode,
            profile.FaceRecognitionEnabled,
            profile.PersonalQrIssuedAt,
            profile.CreatedAt,
            profile.UpdatedAt
        );
}
