using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace smart_locking_be.Infrastructure.Services;

/// <summary>Redacts Vietnamese phone numbers in audit/operational response text, including old records.</summary>
internal static partial class OperationalPhoneMask
{
    // Do not match inside identifiers (including hyphenated IDs), longer numbers or email addresses.
    // Limit separators to one character between digits so matching stays bounded on untrusted free text.
    [GeneratedRegex(@"(?<![\p{L}\p{N}_@+\-])(?:0|\+?84)[ .\-]?[1-9](?:[ .\-]?[0-9]){8}(?![\p{L}\p{N}_@\-])", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [return: NotNullIfNotNull(nameof(value))]
    internal static string? Apply(string? value) => value is null ? null : PhonePattern().Replace(value, match =>
    {
        string digits = string.Concat(match.Value.Where(char.IsAsciiDigit));
        string local = digits.StartsWith("84", StringComparison.Ordinal) ? "0" + digits[2..] : digits;
        return local[..3] + "****" + local[^3..];
    });
}
