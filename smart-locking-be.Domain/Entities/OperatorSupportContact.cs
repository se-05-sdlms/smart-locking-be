namespace smart_locking_be.Domain.Entities;

public sealed class OperatorSupportContact
{
    public Guid OperatorUserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string WorkPhoneNumber { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public int UtcOffsetMinutes { get; set; }
    public int WeekdaysMask { get; set; }
    public int StartsAtMinute { get; set; }
    public int EndsAtMinute { get; set; }
    public string Instructions { get; set; } = string.Empty;
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
