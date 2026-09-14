namespace smart_locking_be.Application.Auth;

public interface ITokenHashService
{
    string CreateSecureToken(int byteLength = 64);

    string HashToken(string token);

    string CreateNumericCode(int length = 6);
}
