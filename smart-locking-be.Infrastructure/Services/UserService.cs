using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Auth;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class UserService(
    ApplicationDbContext dbContext,
    IPasswordHashService passwordHashService,
    ITokenHashService tokenHashService) : IUserService
{
    public async Task<PagedResult<UserListItemResponse>> GetUsersAsync(
        GetUsersFilterRequest filter,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users
            .AsNoTracking()
            .Include(u => u.ResidentProfile)
            .Include(u => u.OperatorAssignments)
            .AsQueryable();

        if (filter.Role.HasValue)
        {
            query = query.Where(u => u.Role == filter.Role.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(u => u.Status == filter.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string search = filter.Search.Trim().ToLower();
            query = query.Where(u =>
                (u.PhoneNumber != null && u.PhoneNumber.Contains(search)) ||
                (u.Email != null && u.Email.ToLower().Contains(search)) ||
                (u.ResidentProfile != null && u.ResidentProfile.FullName.ToLower().Contains(search)));
        }

        int totalCount = await query.CountAsync(cancellationToken);

        int pageNumber = filter.PageNumber < 1 ? 1 : filter.PageNumber;
        int pageSize = filter.PageSize < 1 ? 10 : (filter.PageSize > 100 ? 100 : filter.PageSize);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = users.Select(u => new UserListItemResponse(
            u.Id,
            u.ResidentProfile?.FullName ?? u.PhoneNumber ?? u.Email ?? "Người dùng",
            u.PhoneNumber,
            u.Email,
            u.Role,
            u.Status,
            u.OperatorAssignments.Count(a => a.RevokedAt == null),
            u.CreatedAt,
            u.LastLoginAt
        )).ToList();

        return new PagedResult<UserListItemResponse>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<UserDetailResponse> GetUserByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(u => u.ResidentProfile)
            .Include(u => u.OperatorAssignments)
                .ThenInclude(a => a.Building)
            .Include(u => u.OperatorAssignments)
                .ThenInclude(a => a.LockerCluster)
            .Include(u => u.OperatorAssignments)
                .ThenInclude(a => a.Locker)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
        {
            throw new KeyNotFoundException("Không tìm thấy người dùng.");
        }

        return MapToDetail(user);
    }

    public async Task<CreateUserResponse> CreateUserAsync(
        Guid actorAdminId,
        CreateUserRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ArgumentException("Họ và tên không được để trống.", nameof(request.FullName));
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            throw new ArgumentException("Email không hợp lệ.", nameof(request.Email));
        }

        int scopeCount = (request.BuildingId.HasValue ? 1 : 0) +
                         (request.LockerClusterId.HasValue ? 1 : 0) +
                         (request.LockerId.HasValue ? 1 : 0);

        if (scopeCount > 1)
        {
            throw new ArgumentException("Chỉ được phân công chính xác 1 phạm vi: BuildingId, LockerClusterId, hoặc LockerId.");
        }

        string email = request.Email.Trim().ToLowerInvariant();
        string? phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

        bool exists = await dbContext.Users.AnyAsync(u =>
            u.Email == email || (phone != null && u.PhoneNumber == phone), cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("Email hoặc số điện thoại đã được sử dụng.");
        }

        string temporaryPassword = string.IsNullOrWhiteSpace(request.Password)
            ? GenerateTemporaryPassword()
            : request.Password;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PhoneNumber = phone,
            PasswordHash = passwordHashService.HashPassword(temporaryPassword),
            Role = request.Role,
            Status = UserStatus.Active,
            MustChangePassword = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Users.Add(user);

        var profile = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FullName = request.FullName.Trim(),
            DeliveryApprovalMode = DeliveryApprovalMode.Manual,
            PersonalQrTokenHash = tokenHashService.HashToken(tokenHashService.CreateSecureToken()),
            PersonalQrIssuedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ResidentProfiles.Add(profile);

        if (scopeCount == 1 && request.Role == UserRole.LockerOperator)
        {
            var assignment = new OperatorAssignment
            {
                Id = Guid.NewGuid(),
                OperatorUserId = user.Id,
                BuildingId = request.BuildingId,
                LockerClusterId = request.LockerClusterId,
                LockerId = request.LockerId,
                AssignedByUserId = actorAdminId,
                AssignedAt = now,
                Reason = request.AssignmentReason ?? "Phân công ban đầu khi tạo tài khoản"
            };

            dbContext.OperatorAssignments.Add(assignment);
        }

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorAdminId,
            Action = "CreateUser",
            EntityType = "User",
            EntityId = user.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            Details = $"Tạo tài khoản {user.Role}: {email}, Họ tên: {profile.FullName}",
            OccurredAt = now
        };

        dbContext.AuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateUserResponse(
            user.Id,
            profile.FullName,
            user.Email,
            user.PhoneNumber,
            user.Role,
            user.Status,
            temporaryPassword,
            user.CreatedAt
        );
    }

    public async Task<UserDetailResponse> UpdateUserAsync(
        Guid actorAdminId,
        Guid id,
        UpdateUserRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .Include(u => u.ResidentProfile)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
        {
            throw new KeyNotFoundException("Không tìm thấy người dùng.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ArgumentException("Họ và tên không được để trống.", nameof(request.FullName));
        }

        string? email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        string? phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

        if (email != null && email != user.Email)
        {
            bool emailExists = await dbContext.Users.AnyAsync(u => u.Id != id && u.Email == email, cancellationToken);
            if (emailExists)
            {
                throw new InvalidOperationException("Email đã được sử dụng bởi tài khoản khác.");
            }
            user.Email = email;
        }

        if (phone != null && phone != user.PhoneNumber)
        {
            bool phoneExists = await dbContext.Users.AnyAsync(u => u.Id != id && u.PhoneNumber == phone, cancellationToken);
            if (phoneExists)
            {
                throw new InvalidOperationException("Số điện thoại đã được sử dụng bởi tài khoản khác.");
            }
            user.PhoneNumber = phone;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (user.ResidentProfile is null)
        {
            user.ResidentProfile = new ResidentProfile
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FullName = request.FullName.Trim(),
                DeliveryApprovalMode = DeliveryApprovalMode.Manual,
                PersonalQrTokenHash = tokenHashService.HashToken(tokenHashService.CreateSecureToken()),
                PersonalQrIssuedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.ResidentProfiles.Add(user.ResidentProfile);
        }
        else
        {
            user.ResidentProfile.FullName = request.FullName.Trim();
            user.ResidentProfile.UpdatedAt = now;
        }

        user.UpdatedAt = now;

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorAdminId,
            Action = "UpdateUser",
            EntityType = "User",
            EntityId = user.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            Details = $"Cập nhật thông tin người dùng {id}: Họ tên: {request.FullName}, Email: {user.Email}, SĐT: {user.PhoneNumber}",
            OccurredAt = now
        };

        dbContext.AuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetUserByIdAsync(id, cancellationToken);
    }

    public async Task<UserDetailResponse> UpdateUserStatusAsync(
        Guid actorAdminId,
        Guid id,
        UpdateUserStatusRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .Include(u => u.ResidentProfile)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
        {
            throw new KeyNotFoundException("Không tìm thấy người dùng.");
        }

        if (user.Status != request.Status)
        {
            var oldStatus = user.Status;
            user.Status = request.Status;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            if (request.Status is UserStatus.Locked or UserStatus.Disabled)
            {
                var activeTokens = await dbContext.RefreshTokens
                    .Where(t => t.UserId == id && t.RevokedAt == null && t.ExpiresAt > DateTimeOffset.UtcNow)
                    .ToListAsync(cancellationToken);

                foreach (var token in activeTokens)
                {
                    token.RevokedAt = DateTimeOffset.UtcNow;
                    token.RevokedByIp = ipAddress;
                }
            }

            string actionName = request.Status switch
            {
                UserStatus.Locked => "LockUser",
                UserStatus.Disabled => "DisableUser",
                _ => "UnlockUser"
            };

            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = actorAdminId,
                Action = actionName,
                EntityType = "User",
                EntityId = user.Id,
                Result = AuditLogResult.Succeeded,
                IpAddress = ipAddress,
                Details = $"Đổi trạng thái tài khoản người dùng từ {oldStatus} sang {request.Status}. Lý do: {request.Reason}",
                OccurredAt = DateTimeOffset.UtcNow
            };

            dbContext.AuditLogs.Add(auditLog);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetUserByIdAsync(id, cancellationToken);
    }

    public async Task<OperatorAssignmentResponse> AssignOperatorScopeAsync(
        Guid actorAdminId,
        Guid operatorId,
        AssignOperatorScopeRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == operatorId && u.Role == UserRole.LockerOperator, cancellationToken);

        if (user is null)
        {
            throw new KeyNotFoundException("Không tìm thấy nhân viên vận hành.");
        }

        int scopeCount = (request.BuildingId.HasValue ? 1 : 0) +
                         (request.LockerClusterId.HasValue ? 1 : 0) +
                         (request.LockerId.HasValue ? 1 : 0);

        if (scopeCount != 1)
        {
            throw new ArgumentException("Phải chỉ định chính xác 1 phạm vi: BuildingId, LockerClusterId, hoặc LockerId.");
        }

        bool activeExists = await dbContext.OperatorAssignments.AnyAsync(a =>
            a.OperatorUserId == operatorId &&
            a.RevokedAt == null &&
            ((request.BuildingId != null && a.BuildingId == request.BuildingId) ||
             (request.LockerClusterId != null && a.LockerClusterId == request.LockerClusterId) ||
             (request.LockerId != null && a.LockerId == request.LockerId)), cancellationToken);

        if (activeExists)
        {
            throw new InvalidOperationException("Nhân viên đã được phân công phạm vi này và phân công vẫn đang có hiệu lực.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var assignment = new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = operatorId,
            BuildingId = request.BuildingId,
            LockerClusterId = request.LockerClusterId,
            LockerId = request.LockerId,
            AssignedByUserId = actorAdminId,
            AssignedAt = now,
            Reason = request.Reason
        };

        dbContext.OperatorAssignments.Add(assignment);

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorAdminId,
            Action = "AssignOperatorScope",
            EntityType = "OperatorAssignment",
            EntityId = assignment.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            Details = $"Phân công phạm vi cho Operator {operatorId}. Lý do: {request.Reason}",
            OccurredAt = now
        };

        dbContext.AuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(cancellationToken);

        string? buildingName = null;
        if (assignment.BuildingId.HasValue)
        {
            buildingName = await dbContext.Buildings
                .Where(b => b.Id == assignment.BuildingId.Value)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? clusterName = null;
        if (assignment.LockerClusterId.HasValue)
        {
            clusterName = await dbContext.LockerClusters
                .Where(c => c.Id == assignment.LockerClusterId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? lockerCode = null;
        if (assignment.LockerId.HasValue)
        {
            lockerCode = await dbContext.Lockers
                .Where(l => l.Id == assignment.LockerId.Value)
                .Select(l => l.Code)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new OperatorAssignmentResponse(
            assignment.Id,
            assignment.BuildingId,
            buildingName,
            assignment.LockerClusterId,
            clusterName,
            assignment.LockerId,
            lockerCode,
            assignment.AssignedAt,
            assignment.RevokedAt,
            assignment.Reason
        );
    }

    public async Task RevokeOperatorScopeAsync(
        Guid actorAdminId,
        Guid operatorId,
        Guid assignmentId,
        string? reason = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var assignment = await dbContext.OperatorAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.OperatorUserId == operatorId, cancellationToken);

        if (assignment is null)
        {
            throw new KeyNotFoundException("Không tìm thấy bản ghi phân công.");
        }

        if (assignment.RevokedAt.HasValue)
        {
            throw new InvalidOperationException("Phân công này đã bị thu hồi trước đó.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        assignment.RevokedAt = now;
        assignment.Reason = reason ?? "Thu hồi phân công bởi quản trị viên";

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorAdminId,
            Action = "RevokeOperatorScope",
            EntityType = "OperatorAssignment",
            EntityId = assignment.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            Details = $"Thu hồi phân công phạm vi {assignment.Id} của Operator {operatorId}. Lý do: {reason}",
            OccurredAt = now
        };

        dbContext.AuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static UserDetailResponse MapToDetail(User user)
    {
        var assignments = user.OperatorAssignments
            .OrderByDescending(a => a.AssignedAt)
            .Select(a => new OperatorAssignmentResponse(
                a.Id,
                a.BuildingId,
                a.Building?.Name,
                a.LockerClusterId,
                a.LockerCluster?.Name,
                a.LockerId,
                a.Locker?.Code,
                a.AssignedAt,
                a.RevokedAt,
                a.Reason
            )).ToList();

        return new UserDetailResponse(
            user.Id,
            user.ResidentProfile?.FullName ?? user.PhoneNumber ?? user.Email ?? "Người dùng",
            user.PhoneNumber,
            user.Email,
            user.Role,
            user.Status,
            user.MustChangePassword,
            user.ResidentProfile?.DateOfBirth,
            user.ResidentProfile?.AvatarUrl,
            user.ResidentProfile?.DeliveryApprovalMode,
            user.ResidentProfile?.FaceRecognitionEnabled ?? false,
            user.ResidentProfile?.PersonalQrIssuedAt,
            user.CreatedAt,
            user.UpdatedAt,
            user.LastLoginAt,
            assignments
        );
    }

    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%^&*";

        char[] chars =
        [
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            special[RandomNumberGenerator.GetInt32(special.Length)]
        ];

        string all = upper + lower + digits + special;
        var remaining = Enumerable.Range(0, 6)
            .Select(_ => all[RandomNumberGenerator.GetInt32(all.Length)]);

        return new string(chars.Concat(remaining).OrderBy(_ => RandomNumberGenerator.GetInt32(100)).ToArray());
    }
}
