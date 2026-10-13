using smart_locking_be.Infrastructure.Auth;
using Xunit;

namespace smart_locking_be.Tests.Auth;

public sealed class TokenHashServiceTests
{
    private readonly Sha256TokenHashService _service = new();

    #region TKN-01: TokenHashService.CreateSecureToken
    [Fact]
    [Trait("Category", "TKN-01")]
    public void CreateSecureToken_UTC01()
    {
        string token = _service.CreateSecureToken();
        Assert.Equal(88, token.Length); // Base64 of 64 bytes is 88 chars
    }

    [Fact]
    [Trait("Category", "TKN-01")]
    public void CreateSecureToken_UTC02()
    {
        string token1 = _service.CreateSecureToken();
        string token2 = _service.CreateSecureToken();
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    [Trait("Category", "TKN-01")]
    public void CreateSecureToken_UTC03()
    {
        string token = _service.CreateSecureToken(1);
        Assert.Equal(4, token.Length); // Base64 of 1 byte is 4 chars
    }

    [Fact]
    [Trait("Category", "TKN-01")]
    public void CreateSecureToken_UTC04()
    {
        string token = _service.CreateSecureToken(0);
        Assert.Equal("", token);
    }

    [Fact]
    [Trait("Category", "TKN-01")]
    public void CreateSecureToken_UTC05()
    {
        Assert.ThrowsAny<ArgumentException>(() => _service.CreateSecureToken(-1));
    }
    #endregion

    #region TKN-02: TokenHashService.HashToken
    [Fact]
    [Trait("Category", "TKN-02")]
    public void HashToken_UTC01()
    {
        string hash = _service.HashToken("token-value");
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    [Trait("Category", "TKN-02")]
    public void HashToken_UTC02()
    {
        string hash1 = _service.HashToken("token-value");
        string hash2 = _service.HashToken("token-value");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    [Trait("Category", "TKN-02")]
    public void HashToken_UTC03()
    {
        string hash1 = _service.HashToken("token-value");
        string hash2 = _service.HashToken("token-value-2");
        Assert.NotEqual(hash1, hash2);
        Assert.Equal(64, hash2.Length);
    }

    [Fact]
    [Trait("Category", "TKN-02")]
    public void HashToken_UTC04()
    {
        string hash = _service.HashToken("");
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    [Trait("Category", "TKN-02")]
    public void HashToken_UTC05()
    {
        Assert.Throws<ArgumentNullException>(() => _service.HashToken(null!));
    }
    #endregion

    #region TKN-03: TokenHashService.CreateNumericCode
    [Fact]
    [Trait("Category", "TKN-03")]
    public void CreateNumericCode_UTC01()
    {
        string code = _service.CreateNumericCode();
        Assert.Equal(6, code.Length);
        int val = int.Parse(code);
        Assert.InRange(val, 100000, 999999);
    }

    [Fact]
    [Trait("Category", "TKN-03")]
    public void CreateNumericCode_UTC02()
    {
        string code = _service.CreateNumericCode(1);
        Assert.Equal(1, code.Length);
        int val = int.Parse(code);
        Assert.InRange(val, 1, 9);
    }

    [Fact]
    [Trait("Category", "TKN-03")]
    public void CreateNumericCode_UTC03()
    {
        string code = _service.CreateNumericCode(9);
        Assert.Equal(9, code.Length);
        int val = int.Parse(code);
        Assert.InRange(val, 100000000, 999999999);
    }

    [Fact]
    [Trait("Category", "TKN-03")]
    public void CreateNumericCode_UTC04()
    {
        string code = _service.CreateNumericCode(0);
        Assert.Equal("0", code);
    }

    [Fact]
    [Trait("Category", "TKN-03")]
    public void CreateNumericCode_UTC05()
    {
        Assert.ThrowsAny<ArgumentException>(() => _service.CreateNumericCode(-1));
    }
    #endregion
}
