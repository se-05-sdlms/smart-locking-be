using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.Infrastructure.Services;

/// <summary>Khóa HMAC riêng từng tủ; điện thoại chỉ chuyển lệnh, không có khóa ký.</summary>
public sealed class HmacBluetoothCommandSigner(IConfiguration configuration) : IBluetoothCommandSigner
{
    public string Sign(string deviceIdentifier, string command)
    {
        string? key = configuration[$"Bluetooth:CommandSigningKeys:{deviceIdentifier}"];
        if (key is null || key.Length != 64 || key.Any(c => !Uri.IsHexDigit(c)))
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Chưa cấu hình khóa xác thực lệnh Bluetooth cho tủ.");
        var fields = command.Split('|');
        bool correctDevice = fields.Length == 9 && fields[0] == "c1" && fields[1] == deviceIdentifier ||
            fields.Length == 7 && fields[0] == "z1" && fields[2] == deviceIdentifier;
        if (!correctDevice || command.Length > 300 || command.Any(c => c < 32 || c > 126))
            throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Lệnh Bluetooth không đúng tủ hoặc định dạng.");
        byte[] signature = HMACSHA256.HashData(Convert.FromHexString(key), Encoding.ASCII.GetBytes("boxora-ble-v2|" + command));
        return $"b2|{Convert.ToHexString(signature).ToLowerInvariant()}|{command}";
    }
}
