namespace smart_locking_be.Application.Interfaces.Services;

public interface IPasswordHashService
{
    string HashPassword(string password);

    bool VerifyPassword(string password, string passwordHash);
}
