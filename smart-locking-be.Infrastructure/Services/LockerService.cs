using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class LockerService(ApplicationDbContext dbContext, IOperationsRealtimeNotifier? realtimeNotifier = null) : ILockerService
{
    public async Task<LockerDetailResponse> UpdateLockerStatusAsync(Guid userId, string userRole, Guid lockerId,
        UpdateLockerStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (request.OperationalStatus is not (LockerOperationalStatus.Operational or LockerOperationalStatus.OutOfService))
            throw BadRequestException.For(nameof(request.OperationalStatus), "Status must be Operational or OutOfService.");
        string reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 500)
            throw BadRequestException.For(nameof(request.Reason), "Reason is required and must not exceed 500 characters.");

        Locker locker = await dbContext.Lockers.Include(item => item.Compartments)
            .SingleOrDefaultAsync(item => item.Id == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");
        if (userRole != nameof(UserRole.Administrator) &&
            (userRole != nameof(UserRole.LockerOperator) || !await dbContext.EffectiveOperatorAssignments(DateTimeOffset.UtcNow).AnyAsync(item =>
                item.OperatorUserId == userId && item.LockerId == lockerId && item.RevokedAt == null,
                cancellationToken)))
            throw new ForbiddenException(LockerErrorCodes.AccessDenied, "You cannot change this locker.");
        if (locker.OperationalStatus == LockerOperationalStatus.Inactive)
            throw new ConflictException(LockerErrorCodes.NotOperational, "An inactive locker cannot be restored here.");
        if (locker.OperationalStatus == request.OperationalStatus)
            return await MapToDetailResponseAsync(locker, cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (request.OperationalStatus == LockerOperationalStatus.OutOfService &&
            (await dbContext.Parcels.AnyAsync(item => item.DeliveryRequest.LockerId == lockerId &&
                (item.Status == ParcelStatus.Stored || item.Status == ParcelStatus.Overdue), cancellationToken) ||
             await dbContext.ReturnRequests.AnyAsync(item => item.LockerId == lockerId &&
                (item.Status == ReturnRequestStatus.Allocated || item.Status == ReturnRequestStatus.Deposited), cancellationToken) ||
             await dbContext.CompartmentReservations.AnyAsync(item => item.LockerCompartment.LockerId == lockerId &&
                item.ReleasedAt == null && item.ExpiresAt > now, cancellationToken)))
            throw new ConflictException(LockerErrorCodes.NotOperational, "Locker has active parcels, returns, or reservations.");

        LockerOperationalStatus previous = locker.OperationalStatus;
        locker.OperationalStatus = request.OperationalStatus;
        locker.UpdatedAt = now;
        dbContext.LockerEvents.Add(new LockerEvent
        {
            Id = Guid.NewGuid(),
            LockerId = lockerId,
            ActorUserId = userId,
            EventType = LockerEventType.OperationalStatusChanged,
            PreviousValue = previous.ToString(),
            NewValue = locker.OperationalStatus.ToString(),
            Severity = LockerEventSeverity.Info,
            Reason = reason,
            OccurredAt = now,
            ReceivedAt = now
        });
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = userId,
            Action = "Locker.StatusChanged",
            EntityType = nameof(Locker),
            EntityId = lockerId,
            Result = AuditLogResult.Succeeded,
            Details = $"{previous} -> {locker.OperationalStatus}; {reason}",
            OccurredAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (realtimeNotifier is not null)
            await realtimeNotifier.PublishToLockerAsync(lockerId, new RealtimeEvent("LockerStatusChanged", lockerId,
                lockerId, locker.OperationalStatus.ToString(), reason, now), cancellationToken);
        return await MapToDetailResponseAsync(locker, cancellationToken);
    }

    public async Task<PublicLockerResponse> GetPublicLockerAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw BadRequestException.For(nameof(code), "Locker code is required.");

        var locker = await dbContext.Lockers.AsNoTracking()
            .Where(item => item.Code == code.Trim())
            .Select(item => new { item.Code, item.Address, item.OperationalStatus })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");
        if (locker.OperationalStatus != LockerOperationalStatus.Operational ||
            !await dbContext.OperationalLockers().AnyAsync(item => item.Code == locker.Code, cancellationToken))
            throw new ConflictException(LockerErrorCodes.NotOperational, "Locker is not operational.");

        return new PublicLockerResponse(locker.Code, locker.Address, true);
    }

    public async Task<IReadOnlyCollection<LockerSummaryResponse>> GetLockersAsync(
        Guid userId,
        string userRole,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Lockers
            .AsNoTracking()
            .Include(l => l.Compartments)
            .AsQueryable();

        if (userRole == nameof(UserRole.Administrator))
        {
            // Administrator được quyền xem tất cả tủ locker
        }
        else if (userRole == nameof(UserRole.LockerOperator))
        {
            var assignedLockerIds = await dbContext.EffectiveOperatorAssignments(DateTimeOffset.UtcNow)
                .AsNoTracking()
                .Where(a => a.OperatorUserId == userId && a.RevokedAt == null)
                .Select(a => a.LockerId)
                .ToListAsync(cancellationToken);

            query = query.Where(l => assignedLockerIds.Contains(l.Id));
        }
        else
        {
            throw new ForbiddenException(LockerErrorCodes.AccessDenied, $"Role '{userRole}' cannot access the locker list.");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(l =>
                l.Code.ToLower().Contains(term) ||
                l.Address.ToLower().Contains(term) ||
                l.DeviceIdentifier.ToLower().Contains(term));
        }

        var lockers = await query
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);
        Guid[] lockerIds = lockers.Select(item => item.Id).ToArray();
        HashSet<Guid> availableLockers = (await dbContext.OperationalLockers()
            .Where(item => lockerIds.Contains(item.Id)).Select(item => item.Id).ToListAsync(cancellationToken)).ToHashSet();
        HashSet<Guid> availableCompartments = (await dbContext.OperationalCompartments()
            .Where(item => lockerIds.Contains(item.LockerId)).Select(item => item.Id).ToListAsync(cancellationToken)).ToHashSet();

        return lockers.Select(l => new LockerSummaryResponse(
            l.Id,
            l.Code,
            l.Address,
            l.RecoveryAddress,
            l.DeviceIdentifier,
            l.OperationalStatus == LockerOperationalStatus.Operational && !availableLockers.Contains(l.Id)
                ? LockerOperationalStatus.OutOfService : l.OperationalStatus,
            l.ConnectionStatus,
            l.LastSeenAt,
            l.Compartments.Count,
            l.Compartments.Count(c => availableCompartments.Contains(c.Id)),
            l.CreatedAt,
            l.UpdatedAt
        )).ToList();
    }

    public async Task<LockerDetailResponse> GetLockerByIdAsync(
        Guid userId,
        string userRole,
        Guid lockerId,
        CancellationToken cancellationToken = default)
    {
        var locker = await dbContext.Lockers
            .AsNoTracking()
            .Include(l => l.Compartments)
            .FirstOrDefaultAsync(l => l.Id == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, $"Locker '{lockerId}' not found.");

        if (userRole == nameof(UserRole.Administrator))
        {
            // Administrator được quyền xem bất kỳ tủ locker nào
        }
        else if (userRole == nameof(UserRole.LockerOperator))
        {
            var isAssigned = await dbContext.EffectiveOperatorAssignments(DateTimeOffset.UtcNow)
                .AsNoTracking()
                .AnyAsync(a => a.OperatorUserId == userId && a.LockerId == lockerId && a.RevokedAt == null, cancellationToken);

            if (!isAssigned)
            {
                throw new ForbiddenException(LockerErrorCodes.AccessDenied, "You cannot access this locker.");
            }
        }
        else
        {
            throw new ForbiddenException(LockerErrorCodes.AccessDenied, $"Role '{userRole}' cannot access locker details.");
        }

        return await MapToDetailResponseAsync(locker, cancellationToken);
    }

    public async Task<LockerDetailResponse> CreateLockerAsync(
        Guid actorUserId,
        CreateLockerRequest request,
        CancellationToken cancellationToken = default)
    {
        var (code, address, recoveryAddress, deviceIdentifier) = ValidateAndTrimLockerInput(
            request.Code, request.Address, request.RecoveryAddress, request.DeviceIdentifier);

        var existsCode = await dbContext.Lockers
            .AnyAsync(l => l.Code == code, cancellationToken);
        if (existsCode)
        {
            throw new ConflictException(LockerErrorCodes.CodeAlreadyExists, $"Locker code '{code}' already exists.");
        }

        var existsDevice = await dbContext.Lockers
            .AnyAsync(l => l.DeviceIdentifier == deviceIdentifier, cancellationToken);
        if (existsDevice)
        {
            throw new ConflictException(LockerErrorCodes.DeviceAlreadyExists, $"Device identifier '{deviceIdentifier}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = code,
            Address = address,
            RecoveryAddress = recoveryAddress,
            DeviceIdentifier = deviceIdentifier,
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Unknown,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Lockers.Add(locker);
        dbContext.AuditLogs.Add(CreateAudit(actorUserId, "Locker.Created", nameof(Locker), locker.Id, null, now));
        await dbContext.SaveChangesAsync(cancellationToken);

        return await MapToDetailResponseAsync(locker, cancellationToken);
    }

    public async Task<LockerDetailResponse> UpdateLockerAsync(
        Guid actorUserId,
        Guid lockerId,
        UpdateLockerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.OperationalStatus))
        {
            throw BadRequestException.For(nameof(request.OperationalStatus), $"Locker operational status '{request.OperationalStatus}' is invalid.");
        }

        var (code, address, recoveryAddress, deviceIdentifier) = ValidateAndTrimLockerInput(
            request.Code, request.Address, request.RecoveryAddress, request.DeviceIdentifier);

        var locker = await dbContext.Lockers
            .Include(l => l.Compartments)
            .FirstOrDefaultAsync(l => l.Id == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, $"Locker '{lockerId}' not found.");

        var existsCode = await dbContext.Lockers
            .AnyAsync(l => l.Code == code && l.Id != lockerId, cancellationToken);
        if (existsCode)
        {
            throw new ConflictException(LockerErrorCodes.CodeAlreadyExists, $"Locker code '{code}' is used by another locker.");
        }

        var existsDevice = await dbContext.Lockers
            .AnyAsync(l => l.DeviceIdentifier == deviceIdentifier && l.Id != lockerId, cancellationToken);
        if (existsDevice)
        {
            throw new ConflictException(LockerErrorCodes.DeviceAlreadyExists, $"Device identifier '{deviceIdentifier}' is used by another locker.");
        }

        if (request.OperationalStatus != LockerOperationalStatus.Operational &&
            locker.OperationalStatus != request.OperationalStatus)
            await EnsureNoActiveOperationsAsync(lockerId, null, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        LockerOperationalStatus previousStatus = locker.OperationalStatus;
        locker.Code = code;
        locker.Address = address;
        locker.RecoveryAddress = recoveryAddress;
        locker.DeviceIdentifier = deviceIdentifier;
        locker.OperationalStatus = request.OperationalStatus;
        locker.UpdatedAt = now;
        dbContext.AuditLogs.Add(CreateAudit(actorUserId, "Locker.Updated", nameof(Locker), locker.Id, null, now));

        if (previousStatus != locker.OperationalStatus)
        {
            dbContext.AuditLogs.Add(CreateAudit(actorUserId, "Locker.StatusChanged", nameof(Locker), locker.Id,
                $"{previousStatus} -> {locker.OperationalStatus}", now));
            dbContext.LockerEvents.Add(new LockerEvent
            {
                Id = Guid.NewGuid(),
                LockerId = locker.Id,
                EventType = LockerEventType.OperationalStatusChanged,
                PreviousValue = previousStatus.ToString(),
                NewValue = locker.OperationalStatus.ToString(),
                Severity = LockerEventSeverity.Info,
                OccurredAt = now,
                ReceivedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await MapToDetailResponseAsync(locker, cancellationToken);
    }

    public async Task<bool> SoftDeleteLockerAsync(Guid actorUserId, Guid lockerId, CancellationToken cancellationToken = default)
    {
        var locker = await dbContext.Lockers
            .FirstOrDefaultAsync(l => l.Id == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, $"Locker '{lockerId}' not found.");

        if (locker.OperationalStatus != LockerOperationalStatus.Inactive)
            await EnsureNoActiveOperationsAsync(lockerId, null, cancellationToken);
        LockerOperationalStatus previousStatus = locker.OperationalStatus;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        locker.OperationalStatus = LockerOperationalStatus.Inactive;
        locker.UpdatedAt = now;
        if (previousStatus != locker.OperationalStatus)
        {
            dbContext.AuditLogs.Add(CreateAudit(actorUserId, "Locker.Deactivated", nameof(Locker), locker.Id,
                $"{previousStatus} -> {locker.OperationalStatus}", now));
            dbContext.LockerEvents.Add(new LockerEvent
            {
                Id = Guid.NewGuid(),
                LockerId = locker.Id,
                EventType = LockerEventType.OperationalStatusChanged,
                PreviousValue = previousStatus.ToString(),
                NewValue = locker.OperationalStatus.ToString(),
                Severity = LockerEventSeverity.Info,
                OccurredAt = now,
                ReceivedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<LockerCompartmentResponse>> GetCompartmentsAsync(
        Guid userId,
        string userRole,
        Guid lockerId,
        CancellationToken cancellationToken = default)
    {
        var locker = await dbContext.Lockers
            .AsNoTracking()
            .Include(l => l.Compartments)
            .FirstOrDefaultAsync(l => l.Id == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, $"Locker '{lockerId}' not found.");

        if (userRole == nameof(UserRole.Administrator))
        {
            // Administrator được quyền xem tất cả các ngăn tủ
        }
        else if (userRole == nameof(UserRole.LockerOperator))
        {
            var isAssigned = await dbContext.EffectiveOperatorAssignments(DateTimeOffset.UtcNow)
                .AsNoTracking()
                .AnyAsync(a => a.OperatorUserId == userId && a.LockerId == lockerId && a.RevokedAt == null, cancellationToken);

            if (!isAssigned)
            {
                throw new ForbiddenException(LockerErrorCodes.AccessDenied, "You cannot access this locker.");
            }
        }
        else
        {
            throw new ForbiddenException(LockerErrorCodes.AccessDenied, $"Role '{userRole}' cannot access locker compartments.");
        }

        HashSet<Guid> availableCompartments = (await dbContext.OperationalCompartments()
            .Where(item => item.LockerId == lockerId).Select(item => item.Id).ToListAsync(cancellationToken)).ToHashSet();
        return locker.Compartments
            .OrderBy(c => c.Code)
            .Select(item => MapCompartmentToResponse(item, availableCompartments.Contains(item.Id)))
            .ToList();
    }

    public async Task<LockerCompartmentResponse> CreateCompartmentAsync(
        Guid actorUserId,
        Guid lockerId,
        CreateCompartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var (code, hardwareCode) = ValidateAndTrimCompartmentInput(request.Code, request.HardwareCode, request.HardwareChannel);

        var locker = await dbContext.Lockers
            .FirstOrDefaultAsync(l => l.Id == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, $"Locker '{lockerId}' not found.");

        if (locker.OperationalStatus == LockerOperationalStatus.Inactive)
        {
            throw new ConflictException(LockerErrorCodes.NotOperational, "An inactive locker cannot receive new compartments.");
        }

        var existsCode = await dbContext.LockerCompartments
            .AnyAsync(c => c.LockerId == lockerId && c.Code == code, cancellationToken);
        if (existsCode)
        {
            throw new ConflictException(LockerErrorCodes.CompartmentAlreadyExists, $"Compartment code '{code}' already exists in this locker.");
        }

        var existsHardwareCode = await dbContext.LockerCompartments
            .AnyAsync(c => c.LockerId == lockerId && c.HardwareCode == hardwareCode, cancellationToken);
        if (existsHardwareCode)
        {
            throw new ConflictException(LockerErrorCodes.CompartmentAlreadyExists, $"Hardware code '{hardwareCode}' already exists in this locker.");
        }

        var existsHardwareChannel = await dbContext.LockerCompartments
            .AnyAsync(c => c.LockerId == lockerId && c.HardwareChannel == request.HardwareChannel, cancellationToken);
        if (existsHardwareChannel)
        {
            throw new ConflictException(LockerErrorCodes.CompartmentAlreadyExists, $"Hardware channel '{request.HardwareChannel}' already exists in this locker.");
        }

        var now = DateTimeOffset.UtcNow;
        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = lockerId,
            Code = code,
            HardwareCode = hardwareCode,
            HardwareChannel = request.HardwareChannel,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Unknown,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.LockerCompartments.Add(compartment);
        dbContext.AuditLogs.Add(CreateAudit(actorUserId, "LockerCompartment.Created", nameof(LockerCompartment), compartment.Id,
            $"LockerId={lockerId}", now));
        await dbContext.SaveChangesAsync(cancellationToken);

        return await MapCompartmentToResponseAsync(compartment, cancellationToken);
    }

    public async Task<LockerCompartmentResponse> UpdateCompartmentAsync(Guid actorUserId, Guid lockerId,
        Guid compartmentId, UpdateCompartmentRequest request, CancellationToken cancellationToken = default)
    {
        var (code, hardwareCode) = ValidateAndTrimCompartmentInput(request.Code, request.HardwareCode, request.HardwareChannel);
        LockerCompartment compartment = await dbContext.LockerCompartments
            .SingleOrDefaultAsync(item => item.Id == compartmentId && item.LockerId == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.InvalidCompartment, "Compartment not found in this locker.");
        await EnsureNoActiveOperationsAsync(lockerId, compartmentId, cancellationToken);
        if (await dbContext.LockerCompartments.AnyAsync(item => item.LockerId == lockerId && item.Id != compartmentId &&
            (item.Code == code || item.HardwareCode == hardwareCode || item.HardwareChannel == request.HardwareChannel), cancellationToken))
            throw new ConflictException(LockerErrorCodes.CompartmentAlreadyExists, "Compartment code or hardware mapping already exists.");
        compartment.Code = code;
        compartment.HardwareCode = hardwareCode;
        compartment.HardwareChannel = request.HardwareChannel;
        compartment.UpdatedAt = DateTimeOffset.UtcNow;
        dbContext.AuditLogs.Add(CreateAudit(actorUserId, "LockerCompartment.Updated", nameof(LockerCompartment),
            compartment.Id, $"LockerId={lockerId}", compartment.UpdatedAt));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await MapCompartmentToResponseAsync(compartment, cancellationToken);
    }

    public async Task<LockerCompartmentResponse> UpdateCompartmentStatusAsync(
        Guid userId,
        string userRole,
        Guid lockerId,
        Guid compartmentId,
        UpdateCompartmentStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.OperationalStatus))
        {
            throw BadRequestException.For(nameof(request.OperationalStatus), $"Compartment operational status '{request.OperationalStatus}' is invalid.");
        }

        var locker = await dbContext.Lockers
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, $"Locker '{lockerId}' not found.");

        if (locker.OperationalStatus == LockerOperationalStatus.Inactive)
        {
            throw new ConflictException(LockerErrorCodes.NotOperational, "An inactive locker cannot update compartment status.");
        }

        if (userRole == nameof(UserRole.Administrator))
        {
            // Administrator được quyền cập nhật
        }
        else if (userRole == nameof(UserRole.LockerOperator))
        {
            var isAssigned = await dbContext.EffectiveOperatorAssignments(DateTimeOffset.UtcNow)
                .AsNoTracking()
                .AnyAsync(a => a.OperatorUserId == userId && a.LockerId == lockerId && a.RevokedAt == null, cancellationToken);

            if (!isAssigned)
            {
                throw new ForbiddenException(LockerErrorCodes.AccessDenied, "You cannot update compartments in this locker.");
            }
        }
        else
        {
            throw new ForbiddenException(LockerErrorCodes.AccessDenied, $"Role '{userRole}' cannot update compartment status.");
        }

        var compartment = await dbContext.LockerCompartments
            .FirstOrDefaultAsync(c => c.Id == compartmentId && c.LockerId == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.InvalidCompartment, $"Compartment '{compartmentId}' was not found in this locker.");

        LockerCompartmentOperationalStatus previousStatus = compartment.OperationalStatus;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (request.OperationalStatus != LockerCompartmentOperationalStatus.Operational &&
            previousStatus != request.OperationalStatus)
            await EnsureNoActiveOperationsAsync(lockerId, compartmentId, cancellationToken);
        compartment.OperationalStatus = request.OperationalStatus;
        compartment.UpdatedAt = now;
        if (previousStatus != compartment.OperationalStatus)
        {
            dbContext.AuditLogs.Add(CreateAudit(userId, "LockerCompartment.StatusChanged", nameof(LockerCompartment),
                compartment.Id, $"{previousStatus} -> {compartment.OperationalStatus}", now));
            dbContext.LockerEvents.Add(new LockerEvent
            {
                Id = Guid.NewGuid(),
                LockerId = lockerId,
                LockerCompartmentId = compartment.Id,
                ActorUserId = userId,
                EventType = LockerEventType.OperationalStatusChanged,
                PreviousValue = previousStatus.ToString(),
                NewValue = compartment.OperationalStatus.ToString(),
                Severity = LockerEventSeverity.Info,
                OccurredAt = now,
                ReceivedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await MapCompartmentToResponseAsync(compartment, cancellationToken);
    }

    private async Task EnsureNoActiveOperationsAsync(Guid lockerId, Guid? compartmentId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IQueryable<Parcel> parcels = dbContext.Parcels.Where(item => item.DeliveryRequest.LockerId == lockerId &&
            (item.Status == ParcelStatus.Stored || item.Status == ParcelStatus.Overdue));
        IQueryable<ReturnRequest> returns = dbContext.ReturnRequests.Where(item => item.LockerId == lockerId &&
            (item.Status == ReturnRequestStatus.Allocated || item.Status == ReturnRequestStatus.Deposited));
        IQueryable<CompartmentReservation> reservations = dbContext.CompartmentReservations.Where(item =>
            item.LockerCompartment.LockerId == lockerId && item.ReleasedAt == null && item.ExpiresAt > now);
        if (compartmentId.HasValue)
        {
            parcels = parcels.Where(item => item.DeliveryRequest.AllocatedCompartmentId == compartmentId);
            returns = returns.Where(item => item.AllocatedCompartmentId == compartmentId);
            reservations = reservations.Where(item => item.LockerCompartmentId == compartmentId);
        }
        if (await parcels.AnyAsync(cancellationToken) || await returns.AnyAsync(cancellationToken) ||
            await reservations.AnyAsync(cancellationToken))
            throw new ConflictException(LockerErrorCodes.ActiveOperations,
                "Locker or compartment has stored parcels, open returns, or active reservations.");
    }

    private static AuditLog CreateAudit(Guid actorUserId, string action, string entityType, Guid entityId,
        string? details, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Result = AuditLogResult.Succeeded,
            Details = details,
            OccurredAt = now
        };

    private static (string Code, string Address, string RecoveryAddress, string DeviceIdentifier) ValidateAndTrimLockerInput(
        string code, string address, string recoveryAddress, string deviceIdentifier)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw BadRequestException.For(nameof(code), "Locker code is required.");
        }
        string trimmedCode = code.Trim();
        if (trimmedCode.Length > 50)
        {
            throw BadRequestException.For(nameof(code), "Locker code must not exceed 50 characters.");
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw BadRequestException.For(nameof(address), "Locker address is required.");
        }
        string trimmedAddress = address.Trim();
        if (trimmedAddress.Length > 500)
        {
            throw BadRequestException.For(nameof(address), "Locker address must not exceed 500 characters.");
        }

        if (string.IsNullOrWhiteSpace(recoveryAddress))
        {
            throw BadRequestException.For(nameof(recoveryAddress), "Recovery address is required.");
        }
        string trimmedRecoveryAddress = recoveryAddress.Trim();
        if (trimmedRecoveryAddress.Length > 500)
        {
            throw BadRequestException.For(nameof(recoveryAddress), "Recovery address must not exceed 500 characters.");
        }

        if (string.IsNullOrWhiteSpace(deviceIdentifier))
        {
            throw BadRequestException.For(nameof(deviceIdentifier), "Device identifier is required.");
        }
        string trimmedDeviceIdentifier = deviceIdentifier.Trim();
        if (trimmedDeviceIdentifier.Length > 100)
        {
            throw BadRequestException.For(nameof(deviceIdentifier), "Device identifier must not exceed 100 characters.");
        }

        return (trimmedCode, trimmedAddress, trimmedRecoveryAddress, trimmedDeviceIdentifier);
    }

    private static (string Code, string HardwareCode) ValidateAndTrimCompartmentInput(string code, string hardwareCode, int hardwareChannel)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw BadRequestException.For(nameof(code), "Compartment code is required.");
        }
        string trimmedCode = code.Trim();
        if (trimmedCode.Length > 50)
        {
            throw BadRequestException.For(nameof(code), "Compartment code must not exceed 50 characters.");
        }

        if (string.IsNullOrWhiteSpace(hardwareCode))
        {
            throw BadRequestException.For(nameof(hardwareCode), "Hardware code is required.");
        }
        string trimmedHardwareCode = hardwareCode.Trim();
        if (trimmedHardwareCode.Length > 100)
        {
            throw BadRequestException.For(nameof(hardwareCode), "Hardware code must not exceed 100 characters.");
        }

        if (hardwareChannel <= 0)
        {
            throw BadRequestException.For(nameof(hardwareChannel), "Hardware channel must be a positive integer.");
        }

        return (trimmedCode, trimmedHardwareCode);
    }

    private async Task<LockerDetailResponse> MapToDetailResponseAsync(Locker locker, CancellationToken cancellationToken)
    {
        bool available = await dbContext.OperationalLockers().AnyAsync(item => item.Id == locker.Id, cancellationToken);
        HashSet<Guid> availableCompartments = (await dbContext.OperationalCompartments()
            .Where(item => item.LockerId == locker.Id).Select(item => item.Id).ToListAsync(cancellationToken)).ToHashSet();
        return new LockerDetailResponse(
            locker.Id,
            locker.Code,
            locker.Address,
            locker.RecoveryAddress,
            locker.DeviceIdentifier,
            locker.OperationalStatus == LockerOperationalStatus.Operational && !available
                ? LockerOperationalStatus.OutOfService : locker.OperationalStatus,
            locker.ConnectionStatus,
            locker.LastSeenAt,
            locker.CreatedAt,
            locker.UpdatedAt,
            locker.Compartments.Select(item => MapCompartmentToResponse(item, availableCompartments.Contains(item.Id))).ToList()
        );
    }

    private async Task<LockerCompartmentResponse> MapCompartmentToResponseAsync(LockerCompartment compartment, CancellationToken cancellationToken) =>
        MapCompartmentToResponse(compartment,
            await dbContext.OperationalCompartments().AnyAsync(item => item.Id == compartment.Id, cancellationToken));

    private static LockerCompartmentResponse MapCompartmentToResponse(LockerCompartment c, bool available)
    {
        return new LockerCompartmentResponse(
            c.Id,
            c.LockerId,
            c.Code,
            c.HardwareCode,
            c.HardwareChannel,
            c.OperationalStatus == LockerCompartmentOperationalStatus.Operational && !available
                ? LockerCompartmentOperationalStatus.OutOfService : c.OperationalStatus,
            c.DoorStatus,
            c.CreatedAt,
            c.UpdatedAt
        );
    }
}
