namespace smart_locking_be.Infrastructure.Options;

/// <summary>
/// Hạn xử lý incident (SLA) tính theo giờ thực 24/7, đơn vị phút, theo mức ưu tiên suy ra từ Incident.Type.
/// </summary>
public sealed class IncidentSlaOptions
{
    public const string SectionName = "IncidentSla";

    public bool Enabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 300;
    public int WarningPercent { get; set; } = 80;

    public SlaTargets Critical { get; set; } = new() { AcknowledgeMinutes = 15, ResolveMinutes = 60 };
    public SlaTargets High { get; set; } = new() { AcknowledgeMinutes = 30, ResolveMinutes = 240 };
    public SlaTargets Medium { get; set; } = new() { AcknowledgeMinutes = 120, ResolveMinutes = 1440 };
    public SlaTargets Low { get; set; } = new() { AcknowledgeMinutes = 480, ResolveMinutes = 4320 };

    public SlaTargets For(IncidentPriority priority) => priority switch
    {
        IncidentPriority.Critical => Critical,
        IncidentPriority.High => High,
        IncidentPriority.Low => Low,
        _ => Medium
    };
}

public sealed class SlaTargets
{
    public int AcknowledgeMinutes { get; set; }
    public int ResolveMinutes { get; set; }
}

public enum IncidentPriority
{
    Critical,
    High,
    Medium,
    Low
}
