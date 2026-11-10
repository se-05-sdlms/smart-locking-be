namespace smart_locking_be.Domain.Entities;

// Mỗi tủ có số thứ tự tăng dần để hai đầu gửi trễ/đảo thứ tự vẫn xử lý đúng.
public sealed class OfflineLockerCursor
{
    public string DeviceIdentifier { get; set; } = "";
    public long LastSequence { get; set; }
}
