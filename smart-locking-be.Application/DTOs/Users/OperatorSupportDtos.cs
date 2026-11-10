namespace smart_locking_be.Application.DTOs.Users;

public sealed record SaveOperatorSupportContactRequest(string DisplayName, string WorkPhoneNumber, bool IsPublic,
    int UtcOffsetMinutes, int WeekdaysMask, int StartsAtMinute, int EndsAtMinute, string Instructions);
public sealed record SaveLockerSupportSettingsRequest(string? FallbackName, string? FallbackPhoneNumber,
    string FallbackInstructions, string? FallbackHours);
public sealed record LockerSupportResponse(string LockerCode, bool IsAvailable, bool IsFallback,
    string? ContactName, string? PhoneNumber, string? SupportHours, string Instructions, DateTimeOffset RefreshedAt);
public sealed record OperatorSupportContactResponse(Guid OperatorUserId, string DisplayName, string WorkPhoneNumber,
    bool IsPublic, int UtcOffsetMinutes, int WeekdaysMask, int StartsAtMinute, int EndsAtMinute,
    string Instructions, DateTimeOffset UpdatedAt);
