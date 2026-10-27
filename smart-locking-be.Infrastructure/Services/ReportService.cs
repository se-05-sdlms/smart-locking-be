using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ReportService(ApplicationDbContext dbContext, TimeProvider timeProvider) : IReportService
{
    public const int ExportRowLimit = 50_000;
    private const int MaxRangeDays = 366;
    private const string TimeZoneName = "UTC";

    private sealed record ReportPage(
        int Total,
        List<Dictionary<string, object?>> Rows,
        Dictionary<string, int> Breakdown,
        decimal? Amount);

    #region Summary (kept as is: contract used by the dashboard)

    public async Task<OperationsReportResponse> GetSummaryAsync(
        DateTimeOffset from, DateTimeOffset to, Guid? lockerId = null, CancellationToken cancellationToken = default)
    {
        if (to <= from) throw BadRequestException.For("to", "Report time range is invalid.");
        if (lockerId.HasValue && !await dbContext.Lockers.AnyAsync(item => item.Id == lockerId, cancellationToken))
            throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");
        return new OperationsReportResponse(
            from, to,
            await dbContext.DeliveryRequests.CountAsync(item => item.CreatedAt >= from && item.CreatedAt < to && (!lockerId.HasValue || item.LockerId == lockerId), cancellationToken),
            await dbContext.ReturnRequests.CountAsync(item => item.CreatedAt >= from && item.CreatedAt < to && (!lockerId.HasValue || item.LockerId == lockerId), cancellationToken),
            await dbContext.Parcels.CountAsync(item => item.RetrievedAt >= from && item.RetrievedAt < to && (!lockerId.HasValue || item.DeliveryRequest.LockerId == lockerId), cancellationToken),
            await dbContext.Incidents.CountAsync(item => item.Status != IncidentStatus.Resolved && item.CreatedAt < to && (!lockerId.HasValue || item.LockerId == lockerId), cancellationToken),
            await dbContext.MaintenanceRequests.CountAsync(item => item.CreatedAt >= from && item.CreatedAt < to && (!lockerId.HasValue || item.LockerId == lockerId), cancellationToken),
            await dbContext.EmergencyUnlocks.CountAsync(item => item.RequestedAt >= from && item.RequestedAt < to && (!lockerId.HasValue || item.LockerId == lockerId), cancellationToken));
    }

    #endregion

    public async Task<ReportResponse> GetReportAsync(ReportQuery query, CancellationToken cancellationToken = default)
    {
        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        int page = Math.Max(query.Page, 1);
        return await BuildReportAsync(query, page, pageSize, (page - 1) * pageSize, cancellationToken);
    }

    public async Task<ReportFile> ExportCsvAsync(ReportQuery query, CancellationToken cancellationToken = default)
    {
        ReportResponse report = await BuildReportAsync(query, 1, ExportRowLimit, 0, cancellationToken);
        var csv = new StringBuilder("﻿"); // BOM so Excel opens the UTF-8 file correctly
        csv.AppendLine(string.Join(',', report.Columns.Select(column => Escape(column.Label))));
        foreach (IReadOnlyDictionary<string, object?> row in report.Rows)
        {
            csv.AppendLine(string.Join(',', report.Columns.Select(column =>
                Escape(row.TryGetValue(column.Key, out object? value) ? value : null))));
        }

        string fileName = $"boxora-{ToKebab(query.Type)}-{query.From:yyyyMMdd}-{query.To:yyyyMMdd}.csv";
        return new ReportFile(fileName, "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(csv.ToString()), report.TotalCount > ExportRowLimit);
    }

    private async Task<ReportResponse> BuildReportAsync(
        ReportQuery query, int page, int pageSize, int skip, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(query.Type)) throw BadRequestException.For("type", "Report type is invalid.");
        if (query.To <= query.From) throw BadRequestException.For("to", "Report time range is invalid.");
        if (query.To - query.From > TimeSpan.FromDays(MaxRangeDays))
            throw BadRequestException.For("to", $"Report time range must not exceed {MaxRangeDays} days.");
        if (query.LockerId.HasValue && !await dbContext.Lockers.AnyAsync(item => item.Id == query.LockerId, cancellationToken))
            throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");

        DateTimeOffset from = query.From;
        DateTimeOffset to = query.To;
        Guid? lockerId = query.LockerId;
        string? status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim();

        (ReportColumn[] columns, ReportPage data) = query.Type switch
        {
            ReportType.Parcels => (ParcelColumns, await ParcelsAsync(from, to, lockerId, status, skip, pageSize, cancellationToken)),
            ReportType.Returns => (ReturnColumns, await ReturnsAsync(from, to, lockerId, status, skip, pageSize, cancellationToken)),
            ReportType.LockerUtilization => (UtilizationColumns, await UtilizationAsync(lockerId, status, skip, pageSize, cancellationToken)),
            ReportType.Incidents => (IncidentColumns, await IncidentsAsync(from, to, lockerId, status, skip, pageSize, cancellationToken)),
            ReportType.Maintenance => (MaintenanceColumns, await MaintenanceAsync(from, to, lockerId, status, skip, pageSize, cancellationToken)),
            ReportType.OverdueTransfers => (TransferColumns, await TransfersAsync(from, to, lockerId, status, skip, pageSize, cancellationToken)),
            ReportType.OverdueFees => (FeeColumns, await FeesAsync(from, to, lockerId, status, skip, pageSize, cancellationToken)),
            ReportType.Payments => (PaymentColumns, await PaymentsAsync(from, to, lockerId, status, skip, pageSize, cancellationToken)),
            _ => (ActivityColumns, await ActivitiesAsync(from, to, lockerId, status, skip, pageSize, cancellationToken))
        };

        return new ReportResponse(
            query.Type.ToString(), from, to, TimeZoneName, timeProvider.GetUtcNow(), columns, data.Rows,
            data.Total, page, pageSize, data.Breakdown, data.Amount);
    }

    #region Report types

    private static readonly ReportColumn[] ParcelColumns =
    [
        new("storedAt", "Thời điểm lưu", "datetime"), new("locker", "Locker", "text"), new("parcelCode", "Mã kiện", "text"),
        new("status", "Trạng thái", "text"), new("retrievedAt", "Thời điểm lấy", "datetime"),
        new("maxStorageUntil", "Hạn lưu tối đa", "datetime")
    ];

    private async Task<ReportPage> ParcelsAsync(DateTimeOffset from, DateTimeOffset to, Guid? lockerId, string? status, int skip, int take, CancellationToken ct)
    {
        ParcelStatus? parsed = ParseStatus<ParcelStatus>(status);
        IQueryable<Parcel> source = dbContext.Parcels.AsNoTracking().Where(item => item.StoredAt >= from && item.StoredAt < to);
        if (lockerId.HasValue) source = source.Where(item => item.DeliveryRequest.LockerId == lockerId);
        if (parsed.HasValue) source = source.Where(item => item.Status == parsed.Value);
        return await PageAsync(source, item => item.Status, null,
            ordered => ordered.OrderByDescending(item => item.StoredAt).ThenBy(item => item.Id),
            item => new
            {
                item.StoredAt,
                Locker = item.DeliveryRequest.Locker.Code,
                item.ParcelCode,
                item.Status,
                item.RetrievedAt,
                item.MaxStorageUntil
            },
            row => Row(("storedAt", row.StoredAt), ("locker", row.Locker), ("parcelCode", row.ParcelCode),
                ("status", row.Status.ToString()), ("retrievedAt", row.RetrievedAt), ("maxStorageUntil", row.MaxStorageUntil)),
            skip, take, ct);
    }

    private static readonly ReportColumn[] ReturnColumns =
    [
        new("createdAt", "Thời điểm tạo", "datetime"), new("locker", "Locker", "text"), new("requestId", "Mã yêu cầu", "text"),
        new("status", "Trạng thái", "text"), new("shipperPickedUpAt", "Shipper lấy lúc", "datetime"), new("reason", "Lý do / ghi chú", "text")
    ];

    private async Task<ReportPage> ReturnsAsync(DateTimeOffset from, DateTimeOffset to, Guid? lockerId, string? status, int skip, int take, CancellationToken ct)
    {
        ReturnRequestStatus? parsed = ParseStatus<ReturnRequestStatus>(status);
        IQueryable<ReturnRequest> source = dbContext.ReturnRequests.AsNoTracking().Where(item => item.CreatedAt >= from && item.CreatedAt < to);
        if (lockerId.HasValue) source = source.Where(item => item.LockerId == lockerId);
        if (parsed.HasValue) source = source.Where(item => item.Status == parsed.Value);
        return await PageAsync(source, item => item.Status, null,
            ordered => ordered.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id),
            item => new { item.CreatedAt, Locker = item.Locker.Code, item.Id, item.Status, item.ShipperPickedUpAt, item.ReturnReason, item.FailureReason },
            row => Row(("createdAt", row.CreatedAt), ("locker", row.Locker), ("requestId", row.Id.ToString()),
                ("status", row.Status.ToString()), ("shipperPickedUpAt", row.ShipperPickedUpAt), ("reason", row.FailureReason ?? row.ReturnReason)),
            skip, take, ct);
    }

    private static readonly ReportColumn[] UtilizationColumns =
    [
        new("locker", "Locker", "text"), new("address", "Địa chỉ", "text"), new("status", "Trạng thái", "text"),
        new("connection", "Kết nối", "text"), new("totalCompartments", "Tổng số ngăn", "number"),
        new("usableCompartments", "Ngăn hoạt động", "number"), new("occupiedCompartments", "Ngăn đang có kiện", "number"),
        new("utilizationPercent", "Tỉ lệ sử dụng (%)", "percent")
    ];

    // A snapshot of right now, so the date range does not apply to this report.
    private async Task<ReportPage> UtilizationAsync(Guid? lockerId, string? status, int skip, int take, CancellationToken ct)
    {
        LockerOperationalStatus? parsed = ParseStatus<LockerOperationalStatus>(status);
        IQueryable<Locker> source = dbContext.Lockers.AsNoTracking();
        if (lockerId.HasValue) source = source.Where(item => item.Id == lockerId);
        if (parsed.HasValue) source = source.Where(item => item.OperationalStatus == parsed.Value);
        return await PageAsync(source, item => item.OperationalStatus, null,
            ordered => ordered.OrderBy(item => item.Code),
            item => new
            {
                item.Code,
                item.Address,
                item.OperationalStatus,
                item.ConnectionStatus,
                Total = dbContext.LockerCompartments.Count(compartment => compartment.LockerId == item.Id),
                Usable = dbContext.LockerCompartments.Count(compartment =>
                    compartment.LockerId == item.Id &&
                    compartment.OperationalStatus == LockerCompartmentOperationalStatus.Operational),
                Occupied = dbContext.Parcels
                    .Where(parcel => parcel.DeliveryRequest.LockerId == item.Id &&
                                     (parcel.Status == ParcelStatus.Stored || parcel.Status == ParcelStatus.Overdue) &&
                                     parcel.DeliveryRequest.AllocatedCompartmentId != null)
                    .Select(parcel => parcel.DeliveryRequest.AllocatedCompartmentId)
                    .Distinct()
                    .Count()
            },
            row => Row(("locker", row.Code), ("address", row.Address), ("status", row.OperationalStatus.ToString()),
                ("connection", row.ConnectionStatus.ToString()), ("totalCompartments", row.Total),
                ("usableCompartments", row.Usable), ("occupiedCompartments", row.Occupied),
                ("utilizationPercent", row.Total == 0 ? 0m : Math.Round(row.Occupied * 100m / row.Total, 1))),
            skip, take, ct);
    }

    private static readonly ReportColumn[] IncidentColumns =
    [
        new("createdAt", "Thời điểm tạo", "datetime"), new("locker", "Locker", "text"), new("type", "Loại", "text"),
        new("status", "Trạng thái", "text"), new("title", "Tiêu đề", "text"), new("resolvedAt", "Giải quyết lúc", "datetime"),
        new("escalatedAt", "Escalate lúc", "datetime")
    ];

    private async Task<ReportPage> IncidentsAsync(DateTimeOffset from, DateTimeOffset to, Guid? lockerId, string? status, int skip, int take, CancellationToken ct)
    {
        IncidentStatus? parsed = ParseStatus<IncidentStatus>(status);
        IQueryable<Incident> source = dbContext.Incidents.AsNoTracking().Where(item => item.CreatedAt >= from && item.CreatedAt < to);
        if (lockerId.HasValue) source = source.Where(item => item.LockerId == lockerId);
        if (parsed.HasValue) source = source.Where(item => item.Status == parsed.Value);
        return await PageAsync(source, item => item.Status, null,
            ordered => ordered.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id),
            item => new { item.CreatedAt, Locker = item.Locker == null ? null : item.Locker.Code, item.Type, item.Status, item.Title, item.ResolvedAt, item.EscalatedAt },
            row => Row(("createdAt", row.CreatedAt), ("locker", row.Locker), ("type", row.Type), ("status", row.Status.ToString()),
                ("title", row.Title), ("resolvedAt", row.ResolvedAt), ("escalatedAt", row.EscalatedAt)),
            skip, take, ct);
    }

    private static readonly ReportColumn[] MaintenanceColumns =
    [
        new("createdAt", "Thời điểm tạo", "datetime"), new("locker", "Locker", "text"), new("priority", "Mức ưu tiên", "text"),
        new("status", "Trạng thái", "text"), new("description", "Mô tả", "text"), new("closedAt", "Đóng lúc", "datetime")
    ];

    private async Task<ReportPage> MaintenanceAsync(DateTimeOffset from, DateTimeOffset to, Guid? lockerId, string? status, int skip, int take, CancellationToken ct)
    {
        MaintenanceStatus? parsed = ParseStatus<MaintenanceStatus>(status);
        IQueryable<MaintenanceRequest> source = dbContext.MaintenanceRequests.AsNoTracking().Where(item => item.CreatedAt >= from && item.CreatedAt < to);
        if (lockerId.HasValue) source = source.Where(item => item.LockerId == lockerId);
        if (parsed.HasValue) source = source.Where(item => item.Status == parsed.Value);
        return await PageAsync(source, item => item.Status, null,
            ordered => ordered.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id),
            item => new { item.CreatedAt, Locker = item.Locker.Code, item.Priority, item.Status, item.Description, item.ClosedAt },
            row => Row(("createdAt", row.CreatedAt), ("locker", row.Locker), ("priority", row.Priority.ToString()),
                ("status", row.Status.ToString()), ("description", row.Description), ("closedAt", row.ClosedAt)),
            skip, take, ct);
    }

    private static readonly ReportColumn[] TransferColumns =
    [
        new("removedAt", "Chuyển đi lúc", "datetime"), new("locker", "Locker", "text"), new("parcelCode", "Mã kiện", "text"),
        new("storedAt", "Lưu từ", "datetime"), new("maxStorageUntil", "Hạn lưu tối đa", "datetime"), new("reason", "Lý do", "text")
    ];

    // Parcels an operator removed from a locker, which is how overdue parcels are moved to the collection point.
    private async Task<ReportPage> TransfersAsync(DateTimeOffset from, DateTimeOffset to, Guid? lockerId, string? status, int skip, int take, CancellationToken ct)
    {
        if (status is not null && !string.Equals(status, nameof(ParcelStatus.Removed), StringComparison.OrdinalIgnoreCase))
            throw BadRequestException.For("status", "Overdue transfer reports only contain Removed parcels.");
        IQueryable<Parcel> source = dbContext.Parcels.AsNoTracking()
            .Where(item => item.Status == ParcelStatus.Removed && item.RemovedAt >= from && item.RemovedAt < to);
        if (lockerId.HasValue) source = source.Where(item => item.DeliveryRequest.LockerId == lockerId);
        return await PageAsync(source, item => item.Status, null,
            ordered => ordered.OrderByDescending(item => item.RemovedAt).ThenBy(item => item.Id),
            item => new { item.RemovedAt, Locker = item.DeliveryRequest.Locker.Code, item.ParcelCode, item.StoredAt, item.MaxStorageUntil, item.RemovalReason },
            row => Row(("removedAt", row.RemovedAt), ("locker", row.Locker), ("parcelCode", row.ParcelCode),
                ("storedAt", row.StoredAt), ("maxStorageUntil", row.MaxStorageUntil), ("reason", row.RemovalReason)),
            skip, take, ct);
    }

    private static readonly ReportColumn[] FeeColumns =
    [
        new("createdAt", "Thời điểm tạo", "datetime"), new("locker", "Locker", "text"), new("parcelCode", "Mã kiện", "text"),
        new("status", "Trạng thái", "text"), new("amount", "Số tiền", "money"), new("currency", "Tiền tệ", "text"),
        new("paidAt", "Thanh toán lúc", "datetime")
    ];

    private async Task<ReportPage> FeesAsync(DateTimeOffset from, DateTimeOffset to, Guid? lockerId, string? status, int skip, int take, CancellationToken ct)
    {
        OverdueChargeStatus? parsed = ParseStatus<OverdueChargeStatus>(status);
        IQueryable<OverdueCharge> source = dbContext.OverdueCharges.AsNoTracking().Where(item => item.CreatedAt >= from && item.CreatedAt < to);
        if (lockerId.HasValue) source = source.Where(item => item.Parcel.DeliveryRequest.LockerId == lockerId);
        if (parsed.HasValue) source = source.Where(item => item.Status == parsed.Value);
        return await PageAsync(source, item => item.Status, item => item.Amount,
            ordered => ordered.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id),
            item => new { item.CreatedAt, Locker = item.Parcel.DeliveryRequest.Locker.Code, item.Parcel.ParcelCode, item.Status, item.Amount, item.Currency, item.PaidAt },
            row => Row(("createdAt", row.CreatedAt), ("locker", row.Locker), ("parcelCode", row.ParcelCode),
                ("status", row.Status.ToString()), ("amount", row.Amount), ("currency", row.Currency), ("paidAt", row.PaidAt)),
            skip, take, ct);
    }

    private static readonly ReportColumn[] PaymentColumns =
    [
        new("requestedAt", "Thời điểm yêu cầu", "datetime"), new("locker", "Locker", "text"), new("orderCode", "Mã giao dịch", "text"),
        new("status", "Trạng thái", "text"), new("amount", "Số tiền", "money"), new("currency", "Tiền tệ", "text"),
        new("completedAt", "Hoàn tất lúc", "datetime"), new("failureReason", "Lý do thất bại", "text")
    ];

    // Overdue-fee payments (PayOS). Resident package purchases are not part of this report.
    private async Task<ReportPage> PaymentsAsync(DateTimeOffset from, DateTimeOffset to, Guid? lockerId, string? status, int skip, int take, CancellationToken ct)
    {
        PaymentTransactionStatus? parsed = ParseStatus<PaymentTransactionStatus>(status);
        IQueryable<PaymentTransaction> source = dbContext.PaymentTransactions.AsNoTracking().Where(item => item.RequestedAt >= from && item.RequestedAt < to);
        if (lockerId.HasValue) source = source.Where(item => item.OverdueCharge.Parcel.DeliveryRequest.LockerId == lockerId);
        if (parsed.HasValue) source = source.Where(item => item.Status == parsed.Value);
        return await PageAsync(source, item => item.Status, item => item.Amount,
            ordered => ordered.OrderByDescending(item => item.RequestedAt).ThenBy(item => item.Id),
            item => new
            {
                item.RequestedAt,
                Locker = item.OverdueCharge.Parcel.DeliveryRequest.Locker.Code,
                item.ExternalOrderCode,
                item.Status,
                item.Amount,
                item.Currency,
                item.CompletedAt,
                item.FailureReason
            },
            row => Row(("requestedAt", row.RequestedAt), ("locker", row.Locker), ("orderCode", row.ExternalOrderCode),
                ("status", row.Status.ToString()), ("amount", row.Amount), ("currency", row.Currency),
                ("completedAt", row.CompletedAt), ("failureReason", row.FailureReason)),
            skip, take, ct);
    }

    private static readonly ReportColumn[] ActivityColumns =
    [
        new("occurredAt", "Thời điểm", "datetime"), new("locker", "Locker", "text"), new("eventType", "Sự kiện", "text"),
        new("severity", "Mức độ", "text"), new("details", "Chi tiết", "text")
    ];

    private async Task<ReportPage> ActivitiesAsync(DateTimeOffset from, DateTimeOffset to, Guid? lockerId, string? status, int skip, int take, CancellationToken ct)
    {
        LockerEventSeverity? parsed = ParseStatus<LockerEventSeverity>(status);
        IQueryable<LockerEvent> source = dbContext.LockerEvents.AsNoTracking().Where(item => item.OccurredAt >= from && item.OccurredAt < to);
        if (lockerId.HasValue) source = source.Where(item => item.LockerId == lockerId);
        if (parsed.HasValue) source = source.Where(item => item.Severity == parsed.Value);
        return await PageAsync(source, item => item.Severity, null,
            ordered => ordered.OrderByDescending(item => item.OccurredAt).ThenBy(item => item.Id),
            item => new { item.OccurredAt, Locker = item.Locker.Code, item.EventType, item.Severity, item.Details, item.Reason },
            row => Row(("occurredAt", row.OccurredAt), ("locker", row.Locker), ("eventType", row.EventType.ToString()),
                ("severity", row.Severity.ToString()), ("details", row.Details ?? row.Reason)),
            skip, take, ct);
    }

    #endregion

    #region Helpers

    private static async Task<ReportPage> PageAsync<TEntity, TStatus, TProjection>(
        IQueryable<TEntity> source,
        Expression<Func<TEntity, TStatus>> statusSelector,
        Expression<Func<TEntity, decimal>>? amountSelector,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> order,
        Expression<Func<TEntity, TProjection>> projection,
        Func<TProjection, Dictionary<string, object?>> toRow,
        int skip,
        int take,
        CancellationToken cancellationToken)
        where TStatus : struct, Enum
    {
        int total = await source.CountAsync(cancellationToken);
        var groups = await source
            .GroupBy(statusSelector)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        decimal? amount = amountSelector is null ? null : await source.SumAsync(amountSelector, cancellationToken);
        List<TProjection> items = await order(source).Skip(skip).Take(take).Select(projection).ToListAsync(cancellationToken);
        return new ReportPage(
            total,
            items.Select(toRow).ToList(),
            groups.OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key.ToString(), group => group.Count),
            amount);
    }

    private static T? ParseStatus<T>(string? status) where T : struct, Enum
    {
        if (status is null) return null;
        if (Enum.TryParse(status, ignoreCase: true, out T value) && Enum.IsDefined(value)) return value;
        throw BadRequestException.For("status",
            $"Status must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
    }

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells) =>
        cells.ToDictionary(cell => cell.Key, cell => cell.Value);

    private static string ToKebab(ReportType type) =>
        string.Concat(type.ToString().Select((c, index) => index > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    // CSV cell: quote when needed and neutralise spreadsheet formulas so exported data cannot run as an Excel formula.
    private static string Escape(object? value)
    {
        string text = value switch
        {
            null => string.Empty,
            DateTimeOffset date => date.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
        if (value is string && text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    #endregion
}
