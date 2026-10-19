using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.DeviceInstallations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class DeviceInstallationService(ApplicationDbContext dbContext) : IDeviceInstallationService
{
    public async Task<DeviceInstallationResponse> RegisterAsync(
        Guid userId,
        RegisterDeviceInstallationRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveResidentAsync(userId, cancellationToken);

        string installationId = RequireValue(request.InstallationId, nameof(request.InstallationId), 100);
        string expoPushToken = RequireExpoPushToken(request.ExpoPushToken);
        string platform = NormalizePlatform(request.Platform);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        DeviceInstallation? tokenOwner = await dbContext.DeviceInstallations
            .SingleOrDefaultAsync(device => device.ExpoPushToken == expoPushToken, cancellationToken);
        DeviceInstallation? installation = await dbContext.DeviceInstallations
            .SingleOrDefaultAsync(device => device.InstallationId == installationId, cancellationToken);

        if (tokenOwner is not null && installation is not null && tokenOwner.Id != installation.Id)
        {
            throw new ConflictException("Expo push token đã được đăng ký cho thiết bị khác.");
        }

        installation ??= tokenOwner;
        if (installation is null)
        {
            installation = new DeviceInstallation
            {
                Id = Guid.NewGuid(),
                CreatedAt = now
            };
            dbContext.DeviceInstallations.Add(installation);
        }

        installation.UserId = userId;
        installation.InstallationId = installationId;
        installation.ExpoPushToken = expoPushToken;
        installation.Platform = platform;
        installation.IsActive = true;
        installation.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapResponse(installation);
    }

    public async Task DeactivateAsync(
        Guid userId,
        string installationId,
        CancellationToken cancellationToken = default)
    {
        string normalizedInstallationId = RequireValue(installationId, nameof(installationId), 100);
        DeviceInstallation installation = await dbContext.DeviceInstallations
            .SingleOrDefaultAsync(
                device => device.UserId == userId && device.InstallationId == normalizedInstallationId,
                cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy thiết bị đã đăng ký.");

        installation.IsActive = false;
        installation.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureActiveResidentAsync(Guid userId, CancellationToken cancellationToken)
    {
        bool isActiveResident = await dbContext.Users.AnyAsync(
            user => user.Id == userId && user.Role == UserRole.Resident && user.Status == UserStatus.Active,
            cancellationToken);

        if (!isActiveResident)
        {
            throw new ForbiddenException("Tài khoản Cư dân không hợp lệ hoặc không còn hoạt động.");
        }
    }

    private static string RequireExpoPushToken(string value)
    {
        string token = RequireValue(value, "ExpoPushToken", 256);
        bool isExpoToken =
            (token.StartsWith("ExpoPushToken[", StringComparison.Ordinal) ||
             token.StartsWith("ExponentPushToken[", StringComparison.Ordinal)) &&
            token.EndsWith(']');

        return isExpoToken
            ? token
            : throw new BadRequestException("ExpoPushToken không hợp lệ.");
    }

    private static string NormalizePlatform(string value)
    {
        string platform = RequireValue(value, "Platform", 20);
        if (platform.Equals("android", StringComparison.OrdinalIgnoreCase)) return "Android";
        if (platform.Equals("ios", StringComparison.OrdinalIgnoreCase)) return "iOS";

        throw new BadRequestException("Platform chỉ hỗ trợ Android hoặc iOS.");
    }

    private static string RequireValue(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BadRequestException($"{parameterName} không được để trống.");
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new BadRequestException($"{parameterName} không được vượt quá {maxLength} ký tự.");
    }

    private static DeviceInstallationResponse MapResponse(DeviceInstallation installation) =>
        new(
            installation.Id,
            installation.InstallationId,
            installation.ExpoPushToken,
            installation.Platform,
            installation.IsActive,
            installation.UpdatedAt);
}
