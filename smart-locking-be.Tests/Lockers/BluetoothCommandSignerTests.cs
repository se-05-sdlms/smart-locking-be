using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class BluetoothCommandSignerTests
{
    private static HmacBluetoothCommandSigner Signer(string? key) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bluetooth:CommandSigningKeys:LKR-TEST"] = key
        }).Build());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    public void MissingOrInvalidKey_RejectsCommand(string? key) =>
        Assert.Throws<ConflictException>(() => Signer(key).Sign("LKR-TEST", "c1|LKR-TEST|id|1|R|phone|123456|1792443600|command"));

    [Theory]
    [InlineData("c1|LKR-TEST|id|1|R|phone|123456|1792443600|command")]
    [InlineData("z1|command|LKR-TEST|1|subject|1792443600|-")]
    public void CommandAndReset_AreBoundToExactPayload(string command)
    {
        string key = new('1', 64);
        string envelope = Signer(key).Sign("LKR-TEST", command);
        var parts = envelope.Split('|', 3);
        Assert.Equal("b2", parts[0]);
        Assert.Equal(command, parts[2]);
        Assert.Equal(Convert.ToHexString(HMACSHA256.HashData(Convert.FromHexString(key),
            Encoding.ASCII.GetBytes("boxora-ble-v2|" + command))).ToLowerInvariant(), parts[1]);
    }

    [Theory]
    [InlineData("c1|LKR-OTHER|id|1|R|phone|123456|1792443600|command")]
    [InlineData("r1|resident|phone|1792443600")]
    public void WrongDeviceOrResidentPermit_CannotBeSigned(string command) =>
        Assert.Throws<BadRequestException>(() => Signer(new string('1', 64)).Sign("LKR-TEST", command));
}
