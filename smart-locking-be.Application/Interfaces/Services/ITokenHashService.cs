namespace smart_locking_be.Application.Interfaces.Services;

public interface ITokenHashService
{
    string CreateSecureToken(int byteLength = 64);

    string HashToken(string token);

    string CreateNumericCode(int length = 6);
}
