using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Services;

public sealed class VietnamPhoneNumberTests
{
    [Theory]
    [InlineData("0321234567", "0321234567")]
    [InlineData("0389998888", "0389998888")]
    [InlineData("0521234567", "0521234567")]
    [InlineData("0561234567", "0561234567")]
    [InlineData("0701234567", "0701234567")]
    [InlineData("0791234567", "0791234567")]
    [InlineData("0812345678", "0812345678")]
    [InlineData("0868889999", "0868889999")]
    [InlineData("0901234567", "0901234567")]
    [InlineData("0987654321", "0987654321")]
    public void Normalize_ValidStandardMobileNumbers_ReturnsCanonicalTenDigitFormat(string input, string expected)
    {
        string? result = VietnamPhoneNumber.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("+84912345678", "0912345678")]
    [InlineData("84912345678", "0912345678")]
    [InlineData("+84389998888", "0389998888")]
    [InlineData("84389998888", "0389998888")]
    [InlineData("+84561234567", "0561234567")]
    [InlineData("84561234567", "0561234567")]
    [InlineData("+84791234567", "0791234567")]
    [InlineData("84791234567", "0791234567")]
    [InlineData("+84868889999", "0868889999")]
    [InlineData("84868889999", "0868889999")]
    public void Normalize_WithCountryCodePrefix_ConvertsToLeadingZero(string input, string expected)
    {
        string? result = VietnamPhoneNumber.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("090.123.4567", "0901234567")]
    [InlineData("090-123-4567", "0901234567")]
    [InlineData("090 123 4567", "0901234567")]
    [InlineData("  0901234567  ", "0901234567")]
    [InlineData("+84 90 123 4567", "0901234567")]
    [InlineData("+84.90.123.4567", "0901234567")]
    [InlineData("+84-90-123-4567", "0901234567")]
    [InlineData("(+84) 901-234-567", "0901234567")]
    [InlineData("098.765.4321", "0987654321")]
    [InlineData("038-999-8888", "0389998888")]
    public void Normalize_WithSeparatorsAndFormatting_StripsPunctuationAndWhitespace(string input, string expected)
    {
        string? result = VietnamPhoneNumber.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0\u2003")]
    public void Normalize_NullOrWhitespace_ReturnsNull(string? input)
    {
        string? result = VietnamPhoneNumber.Normalize(input);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("0123456789")]       // Old 11-digit / invalid prefix 01
    [InlineData("02431234567")]      // Landline Hanoi (11 digits, prefix 024)
    [InlineData("02831234567")]      // Landline HCMC (11 digits, prefix 028)
    [InlineData("0412345678")]       // Non-mobile prefix 04
    [InlineData("0612345678")]       // Non-mobile prefix 06
    [InlineData("0012345678")]       // Non-mobile prefix 00
    [InlineData("091234567")]        // 9 digits (too short)
    [InlineData("0912345")]          // 7 digits (too short)
    [InlineData("09")]               // 2 digits (too short)
    [InlineData("09123456789")]      // 11 digits starting with 09 (too long)
    [InlineData("091234567890")]     // 12 digits (too long)
    [InlineData("not-a-phone")]      // Alphabetic only
    [InlineData("abcdefghij")]       // Letters only
    [InlineData("+84")]              // Country code only
    [InlineData("090123456a")]       // Letters mixed with 9 digits
    [InlineData("+1 555 123 4567")]  // Non-Vietnam number
    public void Normalize_InvalidNumbers_ReturnsNull(string input)
    {
        string? result = VietnamPhoneNumber.Normalize(input);
        Assert.Null(result);
    }
}
