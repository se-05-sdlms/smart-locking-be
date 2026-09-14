namespace smart_locking_be.Application.Auth;

public interface IPasswordHashService
{
    string HashPassword(string password);

    bool VerifyPassword(string password, string passwordHash);
}
