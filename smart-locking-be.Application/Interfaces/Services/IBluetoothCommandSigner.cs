namespace smart_locking_be.Application.Interfaces.Services;

/// <summary>Đóng gói lệnh BE đã cấp để ESP xác thực trước khi thực thi qua Bluetooth.</summary>
public interface IBluetoothCommandSigner
{
    string Sign(string deviceIdentifier, string command);
}
