namespace smart_locking_be.Domain.Entities;

public sealed class ResidentBiometric
{
    public Guid Id { get; set; }
    public Guid ResidentProfileId { get; set; }
    public string TemplateReference { get; set; } = string.Empty;
    public DateTimeOffset EnrolledAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ResidentProfile ResidentProfile { get; set; } = null!;
}
