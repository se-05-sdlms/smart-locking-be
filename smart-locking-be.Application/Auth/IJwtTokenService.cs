using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Application.Auth;

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user);
}
