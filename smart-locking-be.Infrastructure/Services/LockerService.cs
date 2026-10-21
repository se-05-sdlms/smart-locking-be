using smart_locking_be.Application.Constants;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class LockerService(ApplicationDbContext dbContext) : ILockerService
{
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
            var assignedLockerIds = await dbContext.OperatorAssignments
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

        return lockers.Select(l => new LockerSummaryResponse(
            l.Id,
            l.Code,
            l.Address,
            l.RecoveryAddress,
            l.DeviceIdentifier,
            l.OperationalStatus,
            l.ConnectionStatus,
            l.LastSeenAt,
            l.Compartments.Count,
            l.Compartments.Count(c => c.OperationalStatus == LockerCompartmentOperationalStatus.Operational),
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
            var isAssigned = await dbContext.OperatorAssignments
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

        return MapToDetailResponse(locker);
    }

    public async Task<LockerDetailResponse> CreateLockerAsync(
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
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToDetailResponse(locker);
    }

    public async Task<LockerDetailResponse> UpdateLockerAsync(
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

        var now = DateTimeOffset.UtcNow;
        LockerOperationalStatus previousStatus = locker.OperationalStatus;
        locker.Code = code;
        locker.Address = address;
        locker.RecoveryAddress = recoveryAddress;
        locker.DeviceIdentifier = deviceIdentifier;
        locker.OperationalStatus = request.OperationalStatus;
        locker.UpdatedAt = now;

        if (previousStatus != locker.OperationalStatus)
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

        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToDetailResponse(locker);
    }

    public async Task<bool> SoftDeleteLockerAsync(Guid lockerId, CancellationToken cancellationToken = default)
    {
        var locker = await dbContext.Lockers
            .FirstOrDefaultAsync(l => l.Id == lockerId, cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, $"Locker '{lockerId}' not found.");

        LockerOperationalStatus previousStatus = locker.OperationalStatus;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        locker.OperationalStatus = LockerOperationalStatus.Inactive;
        locker.UpdatedAt = now;
        if (previousStatus != locker.OperationalStatus)
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
            var isAssigned = await dbContext.OperatorAssignments
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

        return locker.Compartments
            .OrderBy(c => c.Code)
            .Select(MapCompartmentToResponse)
            .ToList();
    }

    public async Task<LockerCompartmentResponse> CreateCompartmentAsync(
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
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapCompartmentToResponse(compartment);
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
            var isAssigned = await dbContext.OperatorAssignments
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

        if (request.OperationalStatus != LockerCompartmentOperationalStatus.OutOfService &&
            await dbContext.AuditLogs.AnyAsync(x => x.EntityId == compartmentId && x.Action == CompartmentResetActions.Requested &&
                !dbContext.LockerEvents.Any(e => e.Id == x.Id), cancellationToken))
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Ngăn đang chờ ESP xác nhận đặt lại; chưa thể bật cấp ngăn.");

        LockerCompartmentOperationalStatus previousStatus = compartment.OperationalStatus;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        compartment.OperationalStatus = request.OperationalStatus;
        compartment.UpdatedAt = now;
        if (previousStatus != compartment.OperationalStatus)
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

        await dbContext.SaveChangesAsync(cancellationToken);

        return MapCompartmentToResponse(compartment);
    }

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

    private static LockerDetailResponse MapToDetailResponse(Locker locker)
    {
        return new LockerDetailResponse(
            locker.Id,
            locker.Code,
            locker.Address,
            locker.RecoveryAddress,
            locker.DeviceIdentifier,
            locker.OperationalStatus,
            locker.ConnectionStatus,
            locker.LastSeenAt,
            locker.CreatedAt,
            locker.UpdatedAt,
            locker.Compartments.Select(MapCompartmentToResponse).ToList()
        );
    }

    private static LockerCompartmentResponse MapCompartmentToResponse(LockerCompartment c)
    {
        return new LockerCompartmentResponse(
            c.Id,
            c.LockerId,
            c.Code,
            c.HardwareCode,
            c.HardwareChannel,
            c.OperationalStatus,
            c.DoorStatus,
            c.CreatedAt,
            c.UpdatedAt
        );
    }
}
