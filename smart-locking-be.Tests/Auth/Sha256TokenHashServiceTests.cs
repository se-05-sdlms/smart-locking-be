using smart_locking_be.Infrastructure.Auth;

namespace smart_locking_be.Tests.Auth;

public sealed class Sha256TokenHashServiceTests
{
    [Fact]
    [Trait("Category", "AUTH-10")]
    public void TokenHash_IsDeterministicAndNumericCodeHasRequestedLength()
    {
        var service = new Sha256TokenHashService();

        Assert.Equal(service.HashToken("token"), service.HashToken("token"));
        Assert.NotEqual(service.HashToken("token"), service.HashToken("other"));
        Assert.Matches("^[0-9]{6}$", service.CreateNumericCode());
        Assert.Matches("^[0-9]{4}$", service.CreateNumericCode(4));
        Assert.NotEqual(service.CreateSecureToken(), service.CreateSecureToken());
    }
}
