using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ReportService(ApplicationDbContext dbContext) : IReportService
{
    public async Task<OperationsReportResponse> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from) throw new ArgumentException("Khoảng thời gian báo cáo không hợp lệ.");
        return new OperationsReportResponse(
            from, to,
            await dbContext.DeliveryRequests.CountAsync(item => item.CreatedAt >= from && item.CreatedAt < to, cancellationToken),
            await dbContext.ReturnRequests.CountAsync(item => item.CreatedAt >= from && item.CreatedAt < to, cancellationToken),
            await dbContext.Parcels.CountAsync(item => item.RetrievedAt >= from && item.RetrievedAt < to, cancellationToken),
            await dbContext.Incidents.CountAsync(item => item.Status != IncidentStatus.Resolved && item.CreatedAt < to, cancellationToken),
            await dbContext.MaintenanceRequests.CountAsync(item => item.CreatedAt >= from && item.CreatedAt < to, cancellationToken),
            await dbContext.EmergencyUnlocks.CountAsync(item => item.RequestedAt >= from && item.RequestedAt < to, cancellationToken));
    }
}
