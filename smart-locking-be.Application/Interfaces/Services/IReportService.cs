using smart_locking_be.Application.DTOs.Operations;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IReportService
{
    Task<OperationsReportResponse> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, Guid? lockerId = null, CancellationToken cancellationToken = default);
}
