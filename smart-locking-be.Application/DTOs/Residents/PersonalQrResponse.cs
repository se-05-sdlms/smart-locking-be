namespace smart_locking_be.Application.DTOs.Residents;

public sealed record PersonalQrResponse(
    string QrToken,
    DateTimeOffset IssuedAt
);
