using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using System.Globalization;
using System.Text.RegularExpressions;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Infrastructure.Services;

/// <summary>
/// Chuyển đổi định dạng wire ESP (c1/r1/o1/q1/z1/z2). Chỉ kiểm tra cấu trúc/kiểu dữ liệu;
/// giao thức hiện tại chưa có chữ ký mật mã, nên parse thành công không chứng minh nguồn gửi.
/// Quyền, giao dịch, thứ tự nhật ký và tác động trạng thái phải được service đối chiếu riêng.
/// </summary>
public sealed class BluetoothProtocol : IBluetoothMessageCodec, ICompartmentResetProtocol
{
    public string CreateResetCommand(Guid commandId, string device, int channel, Guid subjectId,
        DateTimeOffset expiresAt, IReadOnlyCollection<Guid> acknowledgedEvents)
    {
        string acks = acknowledgedEvents.Count == 0 ? "-" : string.Join(',', acknowledgedEvents.Select(x => x.ToString("N")));
        return FormattableString.Invariant($"z1|{commandId:N}|{device}|{channel}|{subjectId:N}|{expiresAt.ToUnixTimeSeconds()}|{acks}");
    }

    public CompartmentResetReceipt ParseResetReceipt(string proof)
    {
        if (string.IsNullOrEmpty(proof) || proof.Length > 300)
            throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Phản hồi đặt lại không hợp lệ.");

        string[] fields = proof.Split('|');
        if (fields.Length != 7 || fields[0] != "z2" || !Guid.TryParseExact(fields[1], "N", out var commandId) ||
            !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out int channel) || channel is < 1 or > 4 ||
            !Guid.TryParseExact(fields[4], "N", out var subjectId) || fields[5] is not ("OPEN" or "CLOSED" or "UNKNOWN"))
            throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Phản hồi đặt lại không hợp lệ.");

        if (fields[6] != "OK" && (!fields[6].StartsWith("FAIL_", StringComparison.Ordinal) || fields[6].Length > 80))
            throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Kết quả không hợp lệ.");

        var door = fields[5] == "CLOSED" ? DoorStatus.Closed : fields[5] == "OPEN" ? DoorStatus.Open : DoorStatus.Unknown;
        return new(commandId, fields[2], channel, subjectId, door, fields[6]);
    }

    // Đọc nhật ký hoàn tất sau chu kỳ mở/đóng cửa do firmware ghi nhận.
    public OfflinePinEvidence ParseOfflineEvent(string proof, string device)
    {
        string[] f = ReadMessage(proof, device).Split('|');
        if (f.Length != 12 || f[0] != "o1" || f[3] != device ||
            !Guid.TryParseExact(f[1], "N", out Guid id) || !Guid.TryParseExact(f[2], "N", out Guid deliveryId) ||
            !int.TryParse(f[4], NumberStyles.None, CultureInfo.InvariantCulture, out int channel) || channel is < 1 or > 4 ||
            f[5] is not ("D" or "P" or "R" or "G") || !Regex.IsMatch(f[6], "^0[0-9]{9}$") ||
            (f[5] is "D" or "R" ? !Regex.IsMatch(f[7], "^[0-9]{6}$") : f[7] != "-") ||
            !long.TryParse(f[8], NumberStyles.None, CultureInfo.InvariantCulture, out long opened) || opened < 0 ||
            !long.TryParse(f[9], NumberStyles.None, CultureInfo.InvariantCulture, out long closed) || closed < 0 ||
            !long.TryParse(f[10], NumberStyles.None, CultureInfo.InvariantCulture, out long elapsed) || elapsed < 50 ||
            !long.TryParse(f[11], NumberStyles.None, CultureInfo.InvariantCulture, out long sequence) || sequence < 1 ||
            (opened != 0 && closed != 0 && closed < opened))
            throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Invalid offline PIN event.");
        return new(id, deliveryId, device, channel, f[5], f[6], f[7], opened, closed, elapsed, sequence);
    }

    public (Guid DeliveryId, string Pin) ParsePinProvisioned(string proof, string device)
    {
        string[] fields = ReadMessage(proof, device).Split('|');
        if (fields.Length != 3 || fields[0] != "q1" || !Guid.TryParseExact(fields[1], "N", out Guid id) || !Regex.IsMatch(fields[2], "^[0-9]{6}$"))
            throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Invalid PIN provisioning confirmation.");
        return (id, fields[2]);
    }

    public string CreateOfflineAcknowledgment(string device, Guid eventId) =>
        FormattableString.Invariant($"k1|{eventId:N}");

    // Lệnh mang đúng tủ, ngăn và giao dịch để ESP thực hiện.
    public string CreateCommand(string device, Guid subject, int channel, string kind, string phone, string pin, DateTimeOffset expires, Guid commandId) =>
        FormattableString.Invariant($"c1|{device}|{subject:N}|{channel}|{kind}|{phone}|{pin}|{expires.ToUnixTimeSeconds()}|{commandId:N}");

    public string CreateResidentPermit(string device, Guid userId, string phone, DateTimeOffset expires) =>
        FormattableString.Invariant($"r1|{device}|{userId:N}|{phone}|{expires.ToUnixTimeSeconds()}");

    public (Guid UserId, string Phone) ParseResidentPermit(string permit, string device, DateTimeOffset now)
    {
        string[] f = ReadMessage(permit, device).Split('|');
        if (f.Length != 5 || f[0] != "r1" || (f[1] != "*" && f[1] != device) || !Guid.TryParseExact(f[2], "N", out var user) ||
            !Regex.IsMatch(f[3], "^0[0-9]{9}$") || !long.TryParse(f[4], out long expiry) || expiry <= now.ToUnixTimeSeconds())
            throw new ForbiddenException(BluetoothErrorCodes.AccessDenied, "Quyen resident khong hop le hoac da het han.");
        return (user, f[3]);
    }

    private static string ReadMessage(string value, string device)
    {
        if (!Regex.IsMatch(device, "^[A-Za-z0-9_-]{1,100}$") || string.IsNullOrEmpty(value) || value.Length > 512 ||
            value.Any(c => c < 32 || c > 126))
            throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Invalid Bluetooth message.");
        return value;
    }
}
