using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class LockerService(ApplicationDbContext dbContext) : ILockerService
{
    public async Task<IReadOnlyCollection<RegistrationLockerResponse>> GetRegistrationOptionsAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Lockers
            .AsNoTracking()
            .Where(locker => locker.OperationalStatus == LockerOperationalStatus.Operational)
            .OrderBy(locker => locker.Code)
            .Select(locker => new RegistrationLockerResponse(locker.Id, locker.Code, locker.Address))
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<LockerSummaryResponse>> GetLockersAsync(
        Guid userId,
        string userRole,
        string? search = null,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20)
    {
        IQueryable<Locker> query = ScopeLockers(userId, userRole)
            .AsNoTracking()
            .Include(l => l.Compartments);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(l =>
                l.Code.ToLower().Contains(term) ||
                l.Address.ToLower().Contains(term) ||
                l.DeviceIdentifier.ToLower().Contains(term));
        }

        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        int totalCount = await query.CountAsync(cancellationToken);
        var lockers = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        List<LockerSummaryResponse> items = lockers.Select(l => new LockerSummaryResponse(
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

        return new PagedResult<LockerSummaryResponse>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<PagedResult<OperationalLockerResponse>> GetOperationalSummaryAsync(
        Guid userId,
        string userRole,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20)
    {
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IQueryable<Locker> query = ScopeLockers(userId, userRole).AsNoTracking();
        int totalCount = await query.CountAsync(cancellationToken);
        List<OperationalLockerResponse> items = await query
            .OrderBy(locker => locker.Code)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(locker => new OperationalLockerResponse(
                locker.Id,
                locker.Code,
                locker.Address,
                locker.OperationalStatus,
                locker.ConnectionStatus,
                locker.LastSeenAt,
                locker.Compartments.Count(compartment =>
                    compartment.OperationalStatus == LockerCompartmentOperationalStatus.Operational &&
                    compartment.DoorStatus == DoorStatus.Closed &&
                    !dbContext.Parcels.Any(parcel =>
                        parcel.DeliveryRequest.AllocatedCompartmentId == compartment.Id &&
                        (parcel.Status == ParcelStatus.Stored || parcel.Status == ParcelStatus.Overdue)) &&
                    !dbContext.ReturnRequests.Any(request =>
                        request.AllocatedCompartmentId == compartment.Id &&
                        (request.Status == ReturnRequestStatus.Allocated || request.Status == ReturnRequestStatus.Deposited)) &&
                    !dbContext.CompartmentReservations.Any(reservation =>
                        reservation.LockerCompartmentId == compartment.Id &&
                        reservation.ReleasedAt == null &&
                        reservation.ExpiresAt > now)),
                dbContext.Parcels.Count(parcel =>
                    parcel.DeliveryRequest.LockerId == locker.Id &&
                    (parcel.Status == ParcelStatus.Stored || parcel.Status == ParcelStatus.Overdue)),
                locker.Incidents.Count(incident => incident.Status != IncidentStatus.Resolved)))
            .ToListAsync(cancellationToken);
        return new PagedResult<OperationalLockerResponse>(items, totalCount, pageNumber, pageSize);
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
            ?? throw new KeyNotFoundException($"Locker với ID '{lockerId}' không tồn tại.");

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
                throw new UnauthorizedAccessException("Bạn không có quyền truy cập tủ locker này.");
            }
        }
        else
        {
            throw new UnauthorizedAccessException($"Role '{userRole}' không có quyền truy cập thông tin tủ locker.");
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
            throw new InvalidOperationException($"Mã Locker '{code}' đã tồn tại trong hệ thống.");
        }

        var existsDevice = await dbContext.Lockers
            .AnyAsync(l => l.DeviceIdentifier == deviceIdentifier, cancellationToken);
        if (existsDevice)
        {
            throw new InvalidOperationException($"Mã định danh thiết bị IoT '{deviceIdentifier}' đã tồn tại.");
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
        var (code, address, recoveryAddress, deviceIdentifier) = ValidateAndTrimLockerInput(
            request.Code, request.Address, request.RecoveryAddress, request.DeviceIdentifier);

        var locker = await dbContext.Lockers
            .Include(l => l.Compartments)
            .FirstOrDefaultAsync(l => l.Id == lockerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Locker với ID '{lockerId}' không tồn tại.");

        var existsCode = await dbContext.Lockers
            .AnyAsync(l => l.Code == code && l.Id != lockerId, cancellationToken);
        if (existsCode)
        {
            throw new InvalidOperationException($"Mã Locker '{code}' đã trùng với tủ khác.");
        }

        var existsDevice = await dbContext.Lockers
            .AnyAsync(l => l.DeviceIdentifier == deviceIdentifier && l.Id != lockerId, cancellationToken);
        if (existsDevice)
        {
            throw new InvalidOperationException($"Mã định danh thiết bị '{deviceIdentifier}' đã trùng với tủ khác.");
        }

        var now = DateTimeOffset.UtcNow;
        locker.Code = code;
        locker.Address = address;
        locker.RecoveryAddress = recoveryAddress;
        locker.DeviceIdentifier = deviceIdentifier;
        locker.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToDetailResponse(locker);
    }

    public async Task<PagedResult<LockerCompartmentResponse>> GetCompartmentsAsync(
        Guid userId,
        string userRole,
        Guid lockerId,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20)
    {
        await EnsureAccessAsync(userId, userRole, lockerId, cancellationToken);
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        IQueryable<LockerCompartment> query = dbContext.LockerCompartments
            .AsNoTracking()
            .Where(compartment => compartment.LockerId == lockerId);
        int totalCount = await query.CountAsync(cancellationToken);
        List<LockerCompartmentResponse> items = (await query
            .OrderBy(c => c.Code)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken))
            .Select(MapCompartmentToResponse).ToList();
        return new PagedResult<LockerCompartmentResponse>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<LockerCompartmentResponse> CreateCompartmentAsync(
        Guid lockerId,
        CreateCompartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var (code, hardwareCode) = ValidateAndTrimCompartmentInput(request.Code, request.HardwareCode, request.HardwareChannel);

        var locker = await dbContext.Lockers
            .FirstOrDefaultAsync(l => l.Id == lockerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Locker với ID '{lockerId}' không tồn tại.");

        if (locker.OperationalStatus == LockerOperationalStatus.Inactive)
        {
            throw new InvalidOperationException("Tủ locker đã bị vô hiệu hóa (Inactive), không thể tạo thêm ngăn tủ.");
        }

        var existsCode = await dbContext.LockerCompartments
            .AnyAsync(c => c.LockerId == lockerId && c.Code == code, cancellationToken);
        if (existsCode)
        {
            throw new InvalidOperationException($"Mã ngăn '{code}' đã tồn tại trong tủ locker này.");
        }

        var existsHardwareCode = await dbContext.LockerCompartments
            .AnyAsync(c => c.LockerId == lockerId && c.HardwareCode == hardwareCode, cancellationToken);
        if (existsHardwareCode)
        {
            throw new InvalidOperationException($"Mã phần cứng '{hardwareCode}' đã tồn tại trong tủ locker này.");
        }

        var existsHardwareChannel = await dbContext.LockerCompartments
            .AnyAsync(c => c.LockerId == lockerId && c.HardwareChannel == request.HardwareChannel, cancellationToken);
        if (existsHardwareChannel)
        {
            throw new InvalidOperationException($"Kênh phần cứng (HardwareChannel) '{request.HardwareChannel}' đã tồn tại trong tủ locker này.");
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

    public async Task<LockerDetailResponse> UpdateOperationalStatusAsync(
        Guid userId,
        string userRole,
        Guid lockerId,
        UpdateOperationalStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureAccessAsync(userId, userRole, lockerId, cancellationToken);
        if (!Enum.TryParse(request.Status, true, out LockerOperationalStatus status))
        {
            throw new ArgumentException("Trạng thái tủ không hợp lệ.", nameof(request));
        }

        Locker locker = await dbContext.Lockers
            .Include(item => item.Compartments)
            .SingleAsync(item => item.Id == lockerId, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string previous = locker.OperationalStatus.ToString();
        locker.OperationalStatus = status;
        locker.UpdatedAt = now;
        AddStatusEvent(lockerId, null, userId, previous, status.ToString(), request.Reason, now);
        AddAudit(userId, "Locker.StatusChanged", nameof(Locker), lockerId, $"{previous} -> {status}: {request.Reason}", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapToDetailResponse(locker);
    }

    public async Task<LockerCompartmentResponse> UpdateCompartmentOperationalStatusAsync(
        Guid userId,
        string userRole,
        Guid lockerId,
        Guid compartmentId,
        UpdateOperationalStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse(request.Status, true, out LockerCompartmentOperationalStatus status))
        {
            throw new ArgumentException("Trạng thái ngăn tủ không hợp lệ.", nameof(request));
        }

        await EnsureAccessAsync(userId, userRole, lockerId, cancellationToken);
        Locker locker = await dbContext.Lockers.AsNoTracking().SingleAsync(item => item.Id == lockerId, cancellationToken);

        if (locker.OperationalStatus == LockerOperationalStatus.Inactive)
        {
            throw new InvalidOperationException("Tủ locker đã bị vô hiệu hóa (Inactive), không thể cập nhật trạng thái ngăn tủ.");
        }

        LockerCompartment compartment = await dbContext.LockerCompartments
            .FirstOrDefaultAsync(c => c.Id == compartmentId && c.LockerId == lockerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Ngăn tủ với ID '{compartmentId}' không tồn tại trong tủ locker này.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string previous = compartment.OperationalStatus.ToString();
        compartment.OperationalStatus = status;
        compartment.UpdatedAt = now;
        AddStatusEvent(lockerId, compartment.Id, userId, previous, status.ToString(), request.Reason, now);
        AddAudit(userId, "Compartment.StatusChanged", nameof(LockerCompartment), compartment.Id, $"{previous} -> {status}: {request.Reason}", now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return MapCompartmentToResponse(compartment);
    }

    private async Task EnsureAccessAsync(Guid userId, string userRole, Guid lockerId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Lockers.AnyAsync(item => item.Id == lockerId, cancellationToken))
        {
            throw new KeyNotFoundException($"Locker với ID '{lockerId}' không tồn tại.");
        }
        if (userRole == nameof(UserRole.Administrator)) return;
        if (userRole != nameof(UserRole.LockerOperator) ||
            !await dbContext.OperatorAssignments.AnyAsync(item =>
                item.OperatorUserId == userId && item.LockerId == lockerId && item.RevokedAt == null,
                cancellationToken))
        {
            throw new UnauthorizedAccessException("Bạn không có quyền cập nhật trạng thái tủ locker này.");
        }
    }

    private IQueryable<Locker> ScopeLockers(Guid userId, string userRole) => userRole switch
    {
        nameof(UserRole.Administrator) => dbContext.Lockers,
        nameof(UserRole.LockerOperator) => dbContext.Lockers.Where(locker =>
            dbContext.OperatorAssignments.Any(assignment =>
                assignment.OperatorUserId == userId &&
                assignment.LockerId == locker.Id &&
                assignment.RevokedAt == null)),
        _ => throw new UnauthorizedAccessException($"Role '{userRole}' không có quyền truy cập danh sách tủ locker.")
    };

    private void AddStatusEvent(Guid lockerId, Guid? compartmentId, Guid userId, string previous, string next, string reason, DateTimeOffset now) =>
        dbContext.LockerEvents.Add(new LockerEvent
        {
            Id = Guid.NewGuid(), LockerId = lockerId, LockerCompartmentId = compartmentId, ActorUserId = userId,
            EventType = LockerEventType.OperationalStatusChanged, PreviousValue = previous, NewValue = next,
            Severity = next == "Operational" ? LockerEventSeverity.Info : LockerEventSeverity.Warning,
            Reason = reason?.Trim(), OccurredAt = now, ReceivedAt = now
        });

    private void AddAudit(Guid userId, string action, string entityType, Guid entityId, string details, DateTimeOffset now) =>
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), ActorUserId = userId, Action = action, EntityType = entityType,
            EntityId = entityId, Result = AuditLogResult.Succeeded, Details = details, OccurredAt = now
        });

    private static (string Code, string Address, string RecoveryAddress, string DeviceIdentifier) ValidateAndTrimLockerInput(
        string code, string address, string recoveryAddress, string deviceIdentifier)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Mã Locker không được để trống.", nameof(code));
        }
        string trimmedCode = code.Trim();
        if (trimmedCode.Length > 50)
        {
            throw new ArgumentException("Mã Locker không được vượt quá 50 ký tự.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Địa chỉ tủ không được để trống.", nameof(address));
        }
        string trimmedAddress = address.Trim();
        if (trimmedAddress.Length > 500)
        {
            throw new ArgumentException("Địa chỉ tủ không được vượt quá 500 ký tự.", nameof(address));
        }

        if (string.IsNullOrWhiteSpace(recoveryAddress))
        {
            throw new ArgumentException("Địa chỉ hoàn hàng không được để trống.", nameof(recoveryAddress));
        }
        string trimmedRecoveryAddress = recoveryAddress.Trim();
        if (trimmedRecoveryAddress.Length > 500)
        {
            throw new ArgumentException("Địa chỉ hoàn hàng không được vượt quá 500 ký tự.", nameof(recoveryAddress));
        }

        if (string.IsNullOrWhiteSpace(deviceIdentifier))
        {
            throw new ArgumentException("Mã định danh thiết bị không được để trống.", nameof(deviceIdentifier));
        }
        string trimmedDeviceIdentifier = deviceIdentifier.Trim();
        if (trimmedDeviceIdentifier.Length > 100)
        {
            throw new ArgumentException("Mã định danh thiết bị không được vượt quá 100 ký tự.", nameof(deviceIdentifier));
        }

        return (trimmedCode, trimmedAddress, trimmedRecoveryAddress, trimmedDeviceIdentifier);
    }

    private static (string Code, string HardwareCode) ValidateAndTrimCompartmentInput(string code, string hardwareCode, int hardwareChannel)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Mã ngăn không được để trống.", nameof(code));
        }
        string trimmedCode = code.Trim();
        if (trimmedCode.Length > 50)
        {
            throw new ArgumentException("Mã ngăn không được vượt quá 50 ký tự.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(hardwareCode))
        {
            throw new ArgumentException("Mã phần cứng không được để trống.", nameof(hardwareCode));
        }
        string trimmedHardwareCode = hardwareCode.Trim();
        if (trimmedHardwareCode.Length > 100)
        {
            throw new ArgumentException("Mã phần cứng không được vượt quá 100 ký tự.", nameof(hardwareCode));
        }

        if (hardwareChannel <= 0)
        {
            throw new ArgumentException("Kênh phần cứng (HardwareChannel) phải là số nguyên dương lớn hơn 0.", nameof(hardwareChannel));
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
