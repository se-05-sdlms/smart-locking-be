using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class LockerCommissioningService(
    ApplicationDbContext db, IOptions<LockerCommissioningOptions> options, TimeProvider clock,
    IOperationsRealtimeNotifier? notifier = null) : ILockerCommissioningService
{
    private const string Prefix = "Locker.Commissioning.";

    public async Task<LockerCommissioningResponse> GetAsync(Guid lockerId, CancellationToken cancellationToken = default)
    {
        Locker locker = await db.Lockers.AsNoTracking().Include(item => item.Compartments)
            .SingleOrDefaultAsync(item => item.Id == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");
        LockerCommissioning? state = await db.Set<LockerCommissioning>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.LockerId == lockerId, cancellationToken);
        var history = await db.AuditLogs.AsNoTracking()
            .Where(item => item.EntityId == lockerId && item.Action.StartsWith(Prefix))
            .OrderByDescending(item => item.OccurredAt)
            .Select(item => new CommissioningHistoryResponse(item.ActorUserId, item.Action, item.Details, item.OccurredAt))
            .ToListAsync(cancellationToken);
        IReadOnlyCollection<string> blockers = state is null || state.Stage == LockerCommissioningStage.HandedOver
            ? Array.Empty<string>() : await BlockersAsync(locker, state, cancellationToken);
        return new(locker.Id, state is null, state?.Stage, state?.Revision ?? 0, locker.DeviceIdentifier,
            state?.BoundDeviceIdentifier, state?.FirmwareVersion, state?.FirmwareHardwareModel, state?.ConfiguredAt,
            state?.VerifiedAt, state?.VerifiedByUserId, state?.HandedOverAt, state?.HandedOverByUserId,
            Release(), blockers, history);
    }

    public Task<LockerCommissioningResponse> BindAsync(Guid actorId, Guid lockerId, BindCommissioningDeviceRequest request, CancellationToken cancellationToken = default) =>
        ChangeAsync(actorId, lockerId, request.ExpectedRevision, request.Notes, "DeviceBound", async (locker, state, now) =>
        {
            if (state.Stage != LockerCommissioningStage.Declared)
                throw Invalid("Device binding is already recorded. Resume the current stage.");
            if (string.IsNullOrWhiteSpace(request.DeviceIdentifier) || request.DeviceIdentifier.Trim() != locker.DeviceIdentifier)
                throw Invalid("Confirm the exact device identifier recorded for this locker.");
            if (await db.Lockers.AnyAsync(item => item.Id != lockerId && item.DeviceIdentifier == locker.DeviceIdentifier, cancellationToken))
                throw new ConflictException(LockerErrorCodes.DeviceAlreadyExists, "Device is linked to another locker.");
            state.BoundDeviceIdentifier = locker.DeviceIdentifier;
            state.BoundAt = now;
            state.Stage = LockerCommissioningStage.AwaitingDevice;
        }, cancellationToken);

    public Task<LockerCommissioningResponse> ConfigureAsync(Guid actorId, Guid lockerId, ConfigureCommissioningRequest request, CancellationToken cancellationToken = default) =>
        ChangeAsync(actorId, lockerId, request.ExpectedRevision, request.Notes, "Configured", (locker, state, now) =>
        {
            if (state.Stage is not (LockerCommissioningStage.AwaitingDevice or LockerCommissioningStage.Configuring or LockerCommissioningStage.Verified))
                throw Invalid("Bind the device before recording installation and network configuration.");
            FirmwareReleaseResponse release = Release() ?? throw Invalid("A compatible firmware release and observation freshness policy must be configured by deployment administrators.");
            if (!request.InstallationAndNetworkConfirmed || request.FirmwareVersion != release.Version ||
                request.HardwareModel != release.HardwareModel || !string.Equals(request.FirmwareSha256, release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw Invalid("Confirm the hardware model, downloaded firmware checksum, installation and network setup of the configured release.");
            if (state.BoundDeviceIdentifier != locker.DeviceIdentifier || !Connected(locker, state.BoundAt, now))
                throw Invalid("The bound device must report a fresh online signal after binding.");
            state.FirmwareVersion = release.Version;
            state.FirmwareHardwareModel = release.HardwareModel;
            state.FirmwareSha256 = release.Sha256;
            state.ConfiguredAt = now;
            state.VerifiedAt = null;
            state.VerifiedByUserId = null;
            state.VerifiedConfigurationHash = null;
            state.Stage = LockerCommissioningStage.Configuring;
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<LockerCommissioningResponse> VerifyAsync(Guid actorId, Guid lockerId, VerifyCommissioningRequest request, CancellationToken cancellationToken = default) =>
        ChangeAsync(actorId, lockerId, request.ExpectedRevision, request.Notes, "Verified", async (locker, state, now) =>
        {
            if (state.Stage != LockerCommissioningStage.Configuring)
                throw Invalid("Record configuration before verification.");
            if (!request.EveryDoorPhysicallyTested || !request.WiringAndRelaySafetyConfirmed)
                throw Invalid("An authorized installer must physically check every door, wiring and relay safety.");
            var blockers = await BlockersAsync(locker, state, cancellationToken);
            if (blockers.Count > 0) throw Invalid(string.Join(" ", blockers));
            state.VerifiedConfigurationHash = ConfigurationHash(locker, state);
            state.VerifiedAt = now;
            state.VerifiedByUserId = actorId;
            state.Stage = LockerCommissioningStage.Verified;
        }, cancellationToken);

    public Task<LockerCommissioningResponse> HandoverAsync(Guid actorId, Guid lockerId, HandoverCommissioningRequest request, CancellationToken cancellationToken = default) =>
        ChangeAsync(actorId, lockerId, request.ExpectedRevision, request.Notes, "HandedOver", async (locker, state, now) =>
        {
            if (state.Stage != LockerCommissioningStage.Verified || !request.AcceptedForOperation)
                throw Invalid("Verify the locker and explicitly accept handover before activation.");
            var blockers = await BlockersAsync(locker, state, cancellationToken);
            if (blockers.Count > 0) throw Invalid(string.Join(" ", blockers));
            state.HandedOverAt = now;
            state.HandedOverByUserId = actorId;
            state.Stage = LockerCommissioningStage.HandedOver;
            locker.OperationalStatus = LockerOperationalStatus.Operational;
            locker.UpdatedAt = now;
        }, cancellationToken);

    private async Task<LockerCommissioningResponse> ChangeAsync(Guid actorId, Guid lockerId, int expectedRevision,
        string? notes, string action, Func<Locker, LockerCommissioning, DateTimeOffset, Task> change,
        CancellationToken cancellationToken)
    {
        string note = notes?.Trim() ?? string.Empty;
        if (note.Length > 500) throw BadRequestException.For(nameof(notes), "Notes must not exceed 500 characters. Never enter device secrets.");
        // Parent locker lock is shared with commissioning edits, generic status changes and hardware mapping edits.
        await using var transaction = await LockerCommissioningGuard.LockAsync(db, lockerId, cancellationToken);
        Locker locker = await db.Lockers.Include(item => item.Compartments)
            .SingleOrDefaultAsync(item => item.Id == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");
        LockerCommissioning state = await db.Set<LockerCommissioning>()
            .SingleOrDefaultAsync(item => item.LockerId == lockerId, cancellationToken)
            ?? throw Invalid("Existing lockers are preserved; this workflow applies to newly declared lockers.");
        if (expectedRevision != state.Revision)
            throw Invalid("Commissioning has changed. Reload the latest progress before continuing.");
        if (locker.OperationalStatus != LockerOperationalStatus.OutOfService)
            throw Invalid("Commissioning requires the locker to remain out of service.");
        DateTimeOffset now = clock.GetUtcNow();
        await change(locker, state, now);
        state.Revision++;
        state.UpdatedAt = now;
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorId,
            Action = Prefix + action,
            EntityType = nameof(Locker),
            EntityId = lockerId,
            Result = AuditLogResult.Succeeded,
            Details = $"Stage={state.Stage}; Revision={state.Revision}; Firmware={state.FirmwareVersion}; {note}",
            OccurredAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        if (notifier is not null)
            await notifier.PublishToLockerAsync(lockerId, new RealtimeEvent("LockerCommissioningUpdated", lockerId,
                lockerId, state.Stage.ToString(), null, now), cancellationToken);
        return await GetAsync(lockerId, cancellationToken);
    }

    private async Task<IReadOnlyCollection<string>> BlockersAsync(Locker locker, LockerCommissioning state, CancellationToken cancellationToken)
    {
        List<string> blockers = [];
        DateTimeOffset now = clock.GetUtcNow();
        FirmwareReleaseResponse? release = Release();
        if (release is null) blockers.Add("Compatible firmware release or observation freshness policy is not configured.");
        if (state.BoundDeviceIdentifier != locker.DeviceIdentifier) blockers.Add("Device binding is missing or has changed.");
        if (!state.ConfiguredAt.HasValue) blockers.Add("Installation and network configuration have not been confirmed.");
        if (!Connected(locker, state.ConfiguredAt ?? state.BoundAt, now)) blockers.Add("A fresh online device signal is required.");
        if (release is not null && (state.FirmwareVersion != release.Version || state.FirmwareHardwareModel != release.HardwareModel || state.FirmwareSha256 != release.Sha256))
            blockers.Add("Recorded firmware differs from the configured compatible release; configure again.");
        if (locker.Compartments.Count == 0) blockers.Add("At least one hardware compartment is required.");
        foreach (LockerCompartment slot in locker.Compartments)
        {
            if (slot.HardwareChannel is < 1 or > 4 || slot.OperationalStatus != LockerCompartmentOperationalStatus.Operational ||
                slot.DoorStatus != DoorStatus.Closed || !Fresh(slot.DoorStatusObservedAt, state.ConfiguredAt, now))
                blockers.Add($"Compartment {slot.Code} needs valid hardware mapping, operational status and a fresh closed-door observation after configuration.");
        }
        if (state.VerifiedAt.HasValue && state.VerifiedConfigurationHash != ConfigurationHash(locker, state))
            blockers.Add("Hardware mapping or locker configuration changed after verification; configure and verify again.");
        if (await db.MaintenanceRequests.AnyAsync(item => item.LockerId == locker.Id &&
            (item.Status == MaintenanceStatus.Open || item.Status == MaintenanceStatus.InProgress), cancellationToken))
            blockers.Add("Close or cancel outstanding maintenance before handover.");
        if (await db.Incidents.AnyAsync(item => item.LockerId == locker.Id && item.Status != IncidentStatus.Resolved, cancellationToken))
            blockers.Add("Resolve outstanding incidents before handover.");
        return blockers;
    }

    private bool Connected(Locker locker, DateTimeOffset? since, DateTimeOffset now) =>
        locker.ConnectionStatus == LockerConnectionStatus.Online && Fresh(locker.LastSeenAt, since, now);

    private bool Fresh(DateTimeOffset? observed, DateTimeOffset? since, DateTimeOffset now) =>
        options.Value.ObservationFreshnessSeconds is >= 30 and <= 3600 && observed.HasValue && since.HasValue &&
        observed.Value >= since.Value && observed.Value <= now &&
        observed.Value >= now.AddSeconds(-options.Value.ObservationFreshnessSeconds);

    private FirmwareReleaseResponse? Release()
    {
        LockerCommissioningOptions value = options.Value;
        if (string.IsNullOrWhiteSpace(value.FirmwareVersion) || value.FirmwareVersion.Length > 100 ||
            string.IsNullOrWhiteSpace(value.HardwareModel) || value.HardwareModel.Length > 100 ||
            value.Sha256.Length != 64 || !value.Sha256.All(Uri.IsHexDigit) ||
            value.ObservationFreshnessSeconds is < 30 or > 3600 ||
            !PublicHttpsUrl(value.DownloadUrl) || !PublicHttpsUrl(value.InstallationGuideUrl)) return null;
        return new(value.FirmwareVersion, value.HardwareModel, value.DownloadUrl, value.Sha256.ToUpperInvariant(), value.InstallationGuideUrl);
    }

    private static bool PublicHttpsUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private static string ConfigurationHash(Locker locker, LockerCommissioning state)
    {
        string input = System.Text.Json.JsonSerializer.Serialize(new
        {
            locker.DeviceIdentifier,
            locker.Address,
            locker.RecoveryAddress,
            state.FirmwareVersion,
            state.FirmwareHardwareModel,
            state.FirmwareSha256,
            Slots = locker.Compartments.OrderBy(item => item.Id).Select(item => new { item.Id, item.HardwareCode, item.HardwareChannel })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private static ConflictException Invalid(string message) => new(LockerErrorCodes.NotOperational, message);
}
