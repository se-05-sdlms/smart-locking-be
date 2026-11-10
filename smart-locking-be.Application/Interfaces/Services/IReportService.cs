using smart_locking_be.Application.DTOs.Operations;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IReportService
{
    Task<OperationsReportResponse> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, Guid? lockerId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Báo cáo chi tiết có phân trang theo loại, khoảng thời gian, locker và trạng thái.
    /// </summary>
    Task<ReportResponse> GetReportAsync(ReportQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xuất CSV đúng dữ liệu đang lọc (bỏ phân trang, tối đa ReportService.ExportRowLimit dòng).
    /// </summary>
    Task<ReportFile> ExportCsvAsync(ReportQuery query, CancellationToken cancellationToken = default);
}
