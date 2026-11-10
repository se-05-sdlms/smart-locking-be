using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record FirmwareReleaseResponse(string Version, string HardwareModel, string DownloadUrl, string Sha256, string InstallationGuideUrl);
public sealed record CommissioningHistoryResponse(Guid? ActorUserId, string Action, string? Details, DateTimeOffset OccurredAt);
public sealed record LockerCommissioningResponse(
    Guid LockerId, bool IsLegacyLocker, LockerCommissioningStage? Stage, int Revision,
    string DeviceIdentifier, string? BoundDeviceIdentifier, string? FirmwareVersion, string? FirmwareHardwareModel,
    DateTimeOffset? ConfiguredAt, DateTimeOffset? VerifiedAt, Guid? VerifiedByUserId,
    DateTimeOffset? HandedOverAt, Guid? HandedOverByUserId,
    FirmwareReleaseResponse? FirmwareRelease, IReadOnlyCollection<string> Blockers,
    IReadOnlyCollection<CommissioningHistoryResponse> History);
public sealed record BindCommissioningDeviceRequest(int ExpectedRevision, string DeviceIdentifier, string? Notes);
public sealed record ConfigureCommissioningRequest(int ExpectedRevision, string FirmwareVersion, string HardwareModel, string FirmwareSha256, bool InstallationAndNetworkConfirmed, string? Notes);
public sealed record VerifyCommissioningRequest(int ExpectedRevision, bool EveryDoorPhysicallyTested, bool WiringAndRelaySafetyConfirmed, string? Notes);
public sealed record HandoverCommissioningRequest(int ExpectedRevision, bool AcceptedForOperation, string? Notes);
