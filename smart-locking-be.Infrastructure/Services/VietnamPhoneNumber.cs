using System.Text.RegularExpressions;

namespace smart_locking_be.Infrastructure.Services;

internal static partial class VietnamPhoneNumber
{
    [GeneratedRegex(@"^0(3|5|7|8|9)\d{8}$")]
    private static partial Regex MobilePattern();

    /// <summary>
    /// Chuẩn hóa về dạng 0xxxxxxxxx (bỏ khoảng trắng, dấu chấm, gạch; đổi +84/84 thành 0).
    /// Trả null nếu không phải số di động Việt Nam hợp lệ (đầu số 03, 05, 07, 08, 09).
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string digits = new(value.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length == 11)
        {
            digits = "0" + digits[2..];
        }

        return MobilePattern().IsMatch(digits) ? digits : null;
    }
}
