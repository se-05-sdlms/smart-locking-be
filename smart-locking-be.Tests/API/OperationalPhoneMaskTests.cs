using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.API;

public sealed class OperationalPhoneMaskTests
{
    [Theory]
    [InlineData("0901234001", "090****001")]
    [InlineData("+84901234001", "090****001")]
    [InlineData("84901234001", "090****001")]
    [InlineData("Phone: +84 90 123 4001.", "Phone: 090****001.")]
    [InlineData("090.123.4001 / 090-123-4001", "090****001 / 090****001")]
    [InlineData("0 9 0 1 2 3 4 0 0 1", "090****001")]
    [InlineData("{\"phone\":\"0901234001\"}", "{\"phone\":\"090****001\"}")]
    [InlineData("Liên hệ (0901234001)", "Liên hệ (090****001)")]
    [InlineData("090****001", "090****001")]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("2026-11-17T09:01:23+07:00", "2026-11-17T09:01:23+07:00")]
    [InlineData("09012340-0123-4001-abcd-001122334455", "09012340-0123-4001-abcd-001122334455")]
    [InlineData("PARCEL-0901234001", "PARCEL-0901234001")]
    [InlineData("X0901234001Z", "X0901234001Z")]
    [InlineData("_0901234001_", "_0901234001_")]
    [InlineData("109012340011", "109012340011")]
    [InlineData("0901234001@example.test", "0901234001@example.test")]
    [InlineData("127.0.0.1 / 10.90.123.40", "127.0.0.1 / 10.90.123.40")]
    public void RedactsPhoneTokensAndPreservesOtherData(string? input, string? expected)
    {
        Assert.Equal(expected, OperationalPhoneMask.Apply(input));
        Assert.Equal(expected, OperationalPhoneMask.Apply(OperationalPhoneMask.Apply(input)));
    }
}
