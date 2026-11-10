using smart_locking_be.Application.Interfaces.Services;
using System.Security.Cryptography;
using System.Text;

namespace smart_locking_be.Infrastructure.Auth;

public sealed class Sha256TokenHashService : ITokenHashService
{
    public string CreateSecureToken(int byteLength = 64) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength));

    public string HashToken(string token)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        return Convert.ToHexString(bytes);
    }

    public string CreateNumericCode(int length = 6)
    {
        int min = (int)Math.Pow(10, length - 1);
        int max = (int)Math.Pow(10, length);

        return RandomNumberGenerator.GetInt32(min, max).ToString();
    }
}
