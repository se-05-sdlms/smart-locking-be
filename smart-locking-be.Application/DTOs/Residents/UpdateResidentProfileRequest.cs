namespace smart_locking_be.Application.DTOs.Residents;

public sealed record UpdateResidentProfileRequest(
    string FullName,
    DateOnly? DateOfBirth,
    string? AvatarUrl
);
