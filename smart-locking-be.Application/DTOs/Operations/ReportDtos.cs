namespace smart_locking_be.Application.DTOs.Operations;

/// <summary>
/// Các loại báo cáo chi tiết của Administrator (SRS UC-27).
/// </summary>
public enum ReportType
{
    Parcels,
    Returns,
    LockerUtilization,
    Incidents,
    Maintenance,
    OverdueTransfers,
    OverdueFees,
    Payments,
    OperationalActivities
}

public sealed record ReportQuery(
    ReportType Type,
    DateTimeOffset From,
    DateTimeOffset To,
    Guid? LockerId,
    string? Status,
    int Page,
    int PageSize);

/// <summary>
/// DataType là text, number, money, datetime hoặc percent để giao diện định dạng đúng.
/// </summary>
public sealed record ReportColumn(string Key, string Label, string DataType);

public sealed record ReportResponse(
    string Type,
    DateTimeOffset From,
    DateTimeOffset To,
    string TimeZone,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyDictionary<string, int> StatusBreakdown,
    decimal? TotalAmount);

/// <summary>Truncated = true khi dữ liệu lọc vượt giới hạn xuất và file chỉ chứa các dòng đầu.</summary>
public sealed record ReportFile(string FileName, string ContentType, byte[] Content, bool Truncated);
