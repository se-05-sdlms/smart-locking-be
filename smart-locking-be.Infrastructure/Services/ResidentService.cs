using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ResidentService(ApplicationDbContext dbContext) : IResidentService
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
            throw BadRequestException.For("fullName", "Full name is required.");
        }

        string trimmedFullName = request.FullName.Trim();
        if (trimmedFullName.Length > 150)
        {
            throw BadRequestException.For("fullName", "Full name must not exceed 150 characters.");
        }

        if (request.DateOfBirth.HasValue && request.DateOfBirth.Value > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw BadRequestException.For("dateOfBirth", "Date of birth cannot be in the future.");
        }

        if (!string.IsNullOrWhiteSpace(request.AvatarUrl) && request.AvatarUrl.Trim().Length > 2048)
        {
            throw BadRequestException.For("avatarUrl", "Avatar URL must not exceed 2048 characters.");
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
            throw BadRequestException.For("approvalMode", "Approval mode is invalid.");
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

    private async Task<User> FindUserWithProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .Include(u => u.ResidentProfile)
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(UserErrorCodes.NotFound, "Account not found.");
    }

    private static void ValidateUserAccess(User user)
    {
        if (user.Status == UserStatus.Locked)
        {
            throw new ForbiddenException(UserErrorCodes.Inactive, "Account is locked. Contact an administrator.");
        }

        if (user.Status == UserStatus.Disabled)
        {
            throw new ForbiddenException(UserErrorCodes.Inactive, "Account is disabled.");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenException(UserErrorCodes.Inactive, "Account cannot perform this action.");
        }

        if (user.Role != UserRole.Resident)
        {
            throw new ForbiddenException(UserErrorCodes.Inactive, "User does not have the resident role.");
        }
    }

    private async Task<ResidentProfile> EnsureProfileCreatedAsync(User user, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        // Profile được bổ sung sau đăng ký cũng dùng cùng mặc định do Admin cấu hình.
        DeliveryApprovalMode defaultApprovalMode = await dbContext.SystemPolicies
            .Where(policy => policy.IsActive)
            .Select(policy => (DeliveryApprovalMode?)policy.DefaultApprovalMode)
            .SingleOrDefaultAsync(cancellationToken) ?? DeliveryApprovalMode.Auto;

        string defaultFullName = !string.IsNullOrWhiteSpace(user.PhoneNumber)
            ? user.PhoneNumber
            : (!string.IsNullOrWhiteSpace(user.Email) ? user.Email : "Resident");

        ResidentProfile profile = new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FullName = defaultFullName,
            DeliveryApprovalMode = defaultApprovalMode,
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
            profile.RegisteredLockerId,
            profile.FullName,
            user.PhoneNumber,
            user.Email,
            profile.DateOfBirth,
            profile.AvatarUrl,
            profile.DeliveryApprovalMode,
            profile.FaceRecognitionEnabled,
            profile.CreatedAt,
            profile.UpdatedAt
        );
}
