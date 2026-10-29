using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

/// <summary>Cổng mã hóa/đọc thông điệp ESP; Application làm việc với dữ liệu đã được phân tích.</summary>
public interface ICompartmentResetProtocol
{
    string CreateResetCommand(Guid commandId, string device, int channel, Guid subjectId,
        DateTimeOffset expiresAt, IReadOnlyCollection<Guid> acknowledgedEvents);
    CompartmentResetReceipt ParseResetReceipt(string proof);
}
