using smart_locking_be.Application.Auth;
using smart_locking_be.Infrastructure.Auth;

namespace smart_locking_be.Tests.Auth;

public sealed class TokenHashServiceTests
{
    [Fact]
    public void CreateSecureToken_ReturnsDifferentOpaqueValues()
    {
        ITokenHashService service = new Sha256TokenHashService();

        string first = service.CreateSecureToken();
        string second = service.CreateSecureToken();

        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 64);
    }

    [Fact]
    public void HashToken_IsStableAndNotPlaintext()
    {
        ITokenHashService service = new Sha256TokenHashService();

        string first = service.HashToken("token-value");
        string second = service.HashToken("token-value");

        Assert.Equal(first, second);
        Assert.NotEqual("token-value", first);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void CreateNumericCode_ReturnsSixDigitsByDefault()
    {
        ITokenHashService service = new Sha256TokenHashService();

        string code = service.CreateNumericCode();

        Assert.Matches("^[0-9]{6}$", code);
    }
}
