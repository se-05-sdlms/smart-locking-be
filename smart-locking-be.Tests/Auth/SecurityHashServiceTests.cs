using System.Security.Cryptography;
using smart_locking_be.Infrastructure.Auth;

namespace smart_locking_be.Tests.Auth;

public sealed class PasswordHashServiceTests
{
    private readonly Pbkdf2PasswordHashService service = new();
    private const string Password = "Str0ng-password!";
    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC01() => AssertHash(Password, service.HashPassword(Password));
    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC02() => AssertRequired(null!);
    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC03() => AssertRequired("");
    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC04() => AssertRequired("   ");
    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC05() => AssertHash("a", service.HashPassword("a"));
    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC06()
    { var first = service.HashPassword(Password); var second = service.HashPassword(Password); Assert.NotEqual(first, second); Assert.NotEqual(first.Split('$')[2], second.Split('$')[2]); AssertHash(Password, first); AssertHash(Password, second); }

    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC01() => Assert.True(service.VerifyPassword(Password, KnownHash(Password)));
    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC02() => Assert.False(service.VerifyPassword("wrong-password", KnownHash(Password)));
    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC03() => Assert.False(service.VerifyPassword(null!, KnownHash(Password)));
    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC04() => Assert.False(service.VerifyPassword(Password, null!));
    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC05() => Assert.False(service.VerifyPassword(Password, ""));
    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC06() => Assert.False(service.VerifyPassword(Password, "malformed"));
    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC07() => Assert.False(service.VerifyPassword(Password, "WRONG$1$QQ==$QQ=="));
    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC08() => Assert.False(service.VerifyPassword(Password, "PBKDF2-SHA256$abc$QQ==$QQ=="));
    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC09() => Assert.False(service.VerifyPassword(Password, "PBKDF2-SHA256$1$%%%$%%%"));
    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC10() => Assert.True(service.VerifyPassword("a", KnownHash("a")));

    private void AssertRequired(string value) { var ex = Assert.Throws<ArgumentException>(() => service.HashPassword(value)); Assert.StartsWith("Password is required.", ex.Message); Assert.Equal("password", ex.ParamName); }
    private static void AssertHash(string password, string encoded)
    {
        Assert.NotEqual(password, encoded); var parts = encoded.Split('$'); Assert.Equal(4, parts.Length); Assert.Equal("PBKDF2-SHA256", parts[0]); Assert.Equal("150000", parts[1]);
        var salt = Convert.FromBase64String(parts[2]); var hash = Convert.FromBase64String(parts[3]); Assert.Equal(16, salt.Length); Assert.Equal(32, hash.Length);
        Assert.Equal(Rfc2898DeriveBytes.Pbkdf2(password, salt, 150000, HashAlgorithmName.SHA256, 32), hash);
    }
    private static string KnownHash(string password)
    { byte[] salt = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray(); return $"PBKDF2-SHA256$150000${Convert.ToBase64String(salt)}${Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 150000, HashAlgorithmName.SHA256, 32))}"; }
}

public sealed class TokenHashServiceTests
{
    private readonly Sha256TokenHashService service = new();
    private const string TokenHash = "E6C02A5742EA9D4DE588EB9B9DE7BED43DC17011552186BED3E98B2C5958FF4A";
    [Fact]
    [Trait("Category", "TKN-01")]
    public void CreateSecureToken_UTC01() { var token = service.CreateSecureToken(); Assert.Equal(88, token.Length); Assert.Equal(64, Convert.FromBase64String(token).Length); }
    [Fact]
    [Trait("Category", "TKN-01")]
    public void CreateSecureToken_UTC02() { var first = service.CreateSecureToken(); var second = service.CreateSecureToken(); Assert.NotEqual(first, second); Assert.Equal(64, Convert.FromBase64String(first).Length); Assert.Equal(64, Convert.FromBase64String(second).Length); }
    [Fact]
    [Trait("Category", "TKN-01")]
    public void CreateSecureToken_UTC03() { var token = service.CreateSecureToken(1); Assert.Equal(4, token.Length); Assert.Single(Convert.FromBase64String(token)); }
    [Fact]
    [Trait("Category", "TKN-01")]
    public void CreateSecureToken_UTC04() => Assert.Equal("", service.CreateSecureToken(0));
    [Fact]
    [Trait("Category", "TKN-01")]
    public void CreateSecureToken_UTC05() => Assert.Throws<ArgumentOutOfRangeException>(() => service.CreateSecureToken(-1));
    [Fact]
    [Trait("Category", "TKN-02")]
    public void HashToken_UTC01() => Assert.Equal(TokenHash, service.HashToken("token-value"));
    [Fact]
    [Trait("Category", "TKN-02")]
    public void HashToken_UTC02() { Assert.Equal(TokenHash, service.HashToken("token-value")); Assert.Equal(TokenHash, service.HashToken("token-value")); }
    [Fact]
    [Trait("Category", "TKN-02")]
    public void HashToken_UTC03() => Assert.Equal("27DE0BCB9AE8DA628866BC759A010083566F4551D945A716BF268A3273574E6E", service.HashToken("token-value-2"));
    [Fact]
    [Trait("Category", "TKN-02")]
    public void HashToken_UTC04() => Assert.Equal("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", service.HashToken(""));
    [Fact]
    [Trait("Category", "TKN-02")]
    public void HashToken_UTC05() => Assert.Throws<ArgumentNullException>(() => service.HashToken(null!));
    [Fact]
    [Trait("Category", "TKN-03")]
    public void CreateNumericCode_UTC01() => AssertNumeric(service.CreateNumericCode(), 6, 100000, 999999);
    [Fact]
    [Trait("Category", "TKN-03")]
    public void CreateNumericCode_UTC02() => AssertNumeric(service.CreateNumericCode(1), 1, 1, 9);
    [Fact]
    [Trait("Category", "TKN-03")]
    public void CreateNumericCode_UTC03() => AssertNumeric(service.CreateNumericCode(9), 9, 100000000, 999999999);
    [Fact]
    [Trait("Category", "TKN-03")]
    public void CreateNumericCode_UTC04() => Assert.Equal("0", service.CreateNumericCode(0));
    [Fact]
    [Trait("Category", "TKN-03")]
    public void CreateNumericCode_UTC05() => Assert.Throws<ArgumentException>(() => service.CreateNumericCode(-1));
    private static void AssertNumeric(string value, int length, int min, int max) { Assert.Equal(length, value.Length); Assert.All(value, ch => Assert.InRange(ch, '0', '9')); Assert.InRange(int.Parse(value), min, max); }
}
