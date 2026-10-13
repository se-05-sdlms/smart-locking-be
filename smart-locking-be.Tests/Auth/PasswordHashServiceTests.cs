using smart_locking_be.Infrastructure.Auth;
using Xunit;

namespace smart_locking_be.Tests.Auth;

public sealed class PasswordHashServiceTests
{
    private readonly Pbkdf2PasswordHashService _service = new();

    #region PWD-01: PasswordHashService.HashPassword
    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC01()
    {
        string hash = _service.HashPassword("Str0ng-password!");
        Assert.StartsWith("PBKDF2-SHA256$", hash);
        Assert.DoesNotContain("Str0ng-password!", hash);
    }

    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC02()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.HashPassword(null!));
        Assert.Contains("Password is required.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC03()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.HashPassword(""));
        Assert.Contains("Password is required.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC04()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.HashPassword("   "));
        Assert.Contains("Password is required.", ex.Message);
    }

    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC05()
    {
        string hash = _service.HashPassword("a");
        Assert.StartsWith("PBKDF2-SHA256$", hash);
    }

    [Fact]
    [Trait("Category", "PWD-01")]
    public void HashPassword_UTC06()
    {
        string hash1 = _service.HashPassword("Str0ng-password!");
        string hash2 = _service.HashPassword("Str0ng-password!");
        Assert.NotEqual(hash1, hash2);
    }
    #endregion

    #region PWD-02: PasswordHashService.VerifyPassword
    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC01()
    {
        string hash = _service.HashPassword("Str0ng-password!");
        Assert.True(_service.VerifyPassword("Str0ng-password!", hash));
    }

    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC02()
    {
        string hash = _service.HashPassword("Str0ng-password!");
        Assert.False(_service.VerifyPassword("wrong-password", hash));
    }

    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC03()
    {
        string hash = _service.HashPassword("Str0ng-password!");
        Assert.False(_service.VerifyPassword(null!, hash));
    }

    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC04()
    {
        Assert.False(_service.VerifyPassword("Str0ng-password!", null!));
    }

    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC05()
    {
        Assert.False(_service.VerifyPassword("Str0ng-password!", ""));
    }

    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC06()
    {
        Assert.False(_service.VerifyPassword("Str0ng-password!", "malformed"));
    }

    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC07()
    {
        Assert.False(_service.VerifyPassword("Str0ng-password!", "WRONG$1$QQ==$QQ=="));
    }

    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC08()
    {
        Assert.False(_service.VerifyPassword("Str0ng-password!", "PBKDF2-SHA256$abc$QQ==$QQ=="));
    }

    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC09()
    {
        Assert.False(_service.VerifyPassword("Str0ng-password!", "PBKDF2-SHA256$1$%%%$%%%"));
    }

    [Fact]
    [Trait("Category", "PWD-02")]
    public void VerifyPassword_UTC10()
    {
        string hash = _service.HashPassword("a");
        Assert.True(_service.VerifyPassword("a", hash));
    }
    #endregion
}
