using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Infrastructure.Auth;

namespace smart_locking_be.Tests.Auth;

public sealed class PasswordHashServiceTests
{
    [Fact]
    public void HashPassword_DoesNotReturnPlaintext()
    {
        IPasswordHashService service = new Pbkdf2PasswordHashService();

        string hash = service.HashPassword("Str0ng-password!");

        Assert.NotEqual("Str0ng-password!", hash);
        Assert.StartsWith("PBKDF2-SHA256$", hash);
    }

    [Fact]
    public void VerifyPassword_ReturnsTrueForOriginalPassword()
    {
        IPasswordHashService service = new Pbkdf2PasswordHashService();
        string hash = service.HashPassword("Str0ng-password!");

        Assert.True(service.VerifyPassword("Str0ng-password!", hash));
        Assert.False(service.VerifyPassword("wrong-password", hash));
    }
}
