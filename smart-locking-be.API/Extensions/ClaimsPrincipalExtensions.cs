using System.Security.Claims;

namespace smart_locking_be.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetRequiredUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId)
            ? userId
            : throw new UnauthorizedException(AuthErrorCodes.InvalidToken, "Invalid authentication token.");

    public static (Guid UserId, string Role) GetRequiredIdentity(this ClaimsPrincipal user)
    {
        Guid userId = user.GetRequiredUserId();
        string? role = user.FindFirstValue(ClaimTypes.Role);
        return !string.IsNullOrWhiteSpace(role)
            ? (userId, role)
            : throw new UnauthorizedException(AuthErrorCodes.InvalidToken, "Invalid authentication token.");
    }
}
