namespace smart_locking_be.API.Options;

public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";

    public TokenBucketLimitSettings Global { get; init; } = new();

    public WindowLimitSettings PublicApi { get; init; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60,
        QueueLimit = 0
    };

    public WindowLimitSettings Auth { get; init; } = new()
    {
        PermitLimit = 5,
        WindowSeconds = 60,
        QueueLimit = 0
    };

    public SlidingWindowLimitSettings Search { get; init; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60,
        SegmentsPerWindow = 6,
        QueueLimit = 0
    };

    public ConcurrencyLimitSettings Upload { get; init; } = new()
    {
        PermitLimit = 2,
        QueueLimit = 0
    };

    public ConcurrencyLimitSettings HeavyAction { get; init; } = new()
    {
        PermitLimit = 1,
        QueueLimit = 0
    };

    public SlidingWindowLimitSettings DeviceCommand { get; init; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 60,
        SegmentsPerWindow = 6,
        QueueLimit = 0
    };
}

public sealed class TokenBucketLimitSettings
{
    public int TokenLimit { get; init; } = 100;
    public int TokensPerPeriod { get; init; } = 100;
    public int ReplenishmentPeriodSeconds { get; init; } = 60;
    public int QueueLimit { get; init; } = 0;
}

public class WindowLimitSettings
{
    public int PermitLimit { get; init; } = 30;
    public int WindowSeconds { get; init; } = 60;
    public int QueueLimit { get; init; } = 0;
}

public sealed class SlidingWindowLimitSettings : WindowLimitSettings
{
    public int SegmentsPerWindow { get; init; } = 6;
}

public sealed class ConcurrencyLimitSettings
{
    public int PermitLimit { get; init; } = 2;
    public int QueueLimit { get; init; } = 0;
}
