using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using System.Security.Cryptography;

namespace smart_locking_be.Infrastructure.Services;

public sealed class UserService(
    ApplicationDbContext dbContext,
    IPasswordHashService passwordHashService,
    IEmailSender emailSender) : IUserService
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
            u.ResidentProfile?.FullName ?? u.PhoneNumber ?? u.Email ?? "User",
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
                .ThenInclude(a => a.Locker)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
        {
            throw new NotFoundException(UserErrorCodes.NotFound, "User not found.");
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
            throw BadRequestException.For("fullName", "Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            throw BadRequestException.For("email", "Email is invalid.");
        }

        string email = request.Email.Trim().ToLowerInvariant();
        string? phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

        bool exists = await dbContext.Users.AnyAsync(u =>
            u.Email == email || (phone != null && u.PhoneNumber == phone), cancellationToken);

        if (exists)
        {
            throw new ConflictException(UserErrorCodes.AlreadyExists, "Email or phone number is already in use.");
        }

        string temporaryPassword = request.Role == UserRole.LockerOperator || string.IsNullOrWhiteSpace(request.Password)
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
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ResidentProfiles.Add(profile);

        if (request.LockerId.HasValue && request.Role == UserRole.LockerOperator)
        {
            var assignment = new OperatorAssignment
            {
                Id = Guid.NewGuid(),
                OperatorUserId = user.Id,
                LockerId = request.LockerId.Value,
                AssignedByUserId = actorAdminId,
                AssignedAt = now,
                Reason = request.AssignmentReason ?? "Initial assignment when creating the account"
            };

            dbContext.OperatorAssignments.Add(assignment);
        }

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorAdminId,
            Action = "User.Created",
            EntityType = "User",
            EntityId = user.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            Details = $"Created {user.Role} account",
            OccurredAt = now
        };

        dbContext.AuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAsync(email, "Your Smart Locking account",
                $"Sign in with {email} and temporary password: {temporaryPassword}\nChange it after signing in.", cancellationToken);
        }
        catch (ServiceUnavailableException exception)
        {
            throw new ServiceUnavailableException(UserErrorCodes.EmailUnavailable,
                "Account was created, but the credential email was not delivered. Reissue a temporary password.", exception);
        }

        return new CreateUserResponse(
            user.Id,
            profile.FullName,
            user.Email,
            user.PhoneNumber,
            user.Role,
            user.Status,
            user.CreatedAt
        );
    }

    public async Task ReissueTemporaryPasswordAsync(Guid actorAdminId, Guid operatorId, string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        User user = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == operatorId &&
            item.Role == UserRole.LockerOperator, cancellationToken)
            ?? throw new NotFoundException(UserErrorCodes.NotFound, "Locker operator not found.");
        if (string.IsNullOrWhiteSpace(user.Email))
            throw new ConflictException(UserErrorCodes.EmailUnavailable, "Locker operator has no email address.");

        string temporaryPassword = GenerateTemporaryPassword();

        // Deliver the new password first and only then change the account. If the email cannot be sent,
        // nothing is written: the old password keeps working and the existing sessions stay valid.
        try
        {
            await emailSender.SendAsync(user.Email, "Your new Smart Locking temporary password",
                $"Sign in with {user.Email} and temporary password: {temporaryPassword}\nChange it after signing in.",
                cancellationToken);
        }
        catch (ServiceUnavailableException exception)
        {
            throw new ServiceUnavailableException(UserErrorCodes.EmailUnavailable,
                "The credential email was not delivered, so the password was not changed. Try again.", exception);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        user.PasswordHash = passwordHashService.HashPassword(temporaryPassword);
        user.MustChangePassword = true;
        user.UpdatedAt = now;
        List<RefreshToken> tokens = await dbContext.RefreshTokens.Where(item => item.UserId == user.Id &&
            item.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (RefreshToken token in tokens) token.RevokedAt = now;
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorAdminId,
            Action = "User.TemporaryPasswordReissued",
            EntityType = nameof(User),
            EntityId = user.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            OccurredAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
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
            throw new NotFoundException(UserErrorCodes.NotFound, "User not found.");
        }

        if (user.Role == UserRole.Resident)
        {
            throw new ForbiddenException(UserErrorCodes.ResidentProfileReadOnly,
                "Administrators cannot edit resident personal information. Residents must update their own profile.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw BadRequestException.For("fullName", "Full name is required.");
        }

        string? email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        string? phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

        if (email != null && email != user.Email)
        {
            bool emailExists = await dbContext.Users.AnyAsync(u => u.Id != id && u.Email == email, cancellationToken);
            if (emailExists)
            {
                throw new ConflictException(UserErrorCodes.AlreadyExists, "Email is already used by another account.");
            }
            user.Email = email;
        }

        if (phone != null && phone != user.PhoneNumber)
        {
            bool phoneExists = await dbContext.Users.AnyAsync(u => u.Id != id && u.PhoneNumber == phone, cancellationToken);
            if (phoneExists)
            {
                throw new ConflictException(UserErrorCodes.AlreadyExists, "Phone number is already used by another account.");
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
            Action = "User.Updated",
            EntityType = "User",
            EntityId = user.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            Details = $"Updated user {id}",
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
            throw new NotFoundException(UserErrorCodes.NotFound, "User not found.");
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
                UserStatus.Locked => "User.Locked",
                UserStatus.Disabled => "User.Disabled",
                _ => "User.Unlocked"
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
                Details = $"Changed user account status from {oldStatus} to {request.Status}. Reason: {request.Reason}",
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
        await using var coverageTransaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({EffectiveOperatorAssignmentQueries.AllocationLock})", cancellationToken);

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == operatorId && u.Role == UserRole.LockerOperator, cancellationToken);

        if (user is null)
        {
            throw new NotFoundException(UserErrorCodes.NotFound, "Locker operator not found.");
        }

        if (request.LockerId == Guid.Empty)
        {
            throw BadRequestException.For("lockerId", "A valid LockerId is required.");
        }

        bool activeExists = await dbContext.OperatorAssignments.AnyAsync(a =>
            a.RevokedAt == null &&
            a.LockerId == request.LockerId, cancellationToken);

        if (activeExists)
        {
            throw new ConflictException(UserErrorCodes.AssignmentConflict, "Locker is assigned to another operator.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var assignment = new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = operatorId,
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
            Action = "OperatorAssignment.Created",
            EntityType = "OperatorAssignment",
            EntityId = assignment.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            Details = $"Assigned scope to operator {operatorId}. Reason: {request.Reason}",
            OccurredAt = now
        };

        dbContext.AuditLogs.Add(auditLog);
        await InvalidateCoverageForAssignmentChangeAsync(actorAdminId, operatorId, assignment.LockerId, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (coverageTransaction != null) await coverageTransaction.CommitAsync(cancellationToken);

        string? lockerCode = await dbContext.Lockers
            .Where(l => l.Id == assignment.LockerId)
            .Select(l => l.Code)
            .FirstOrDefaultAsync(cancellationToken);

        return new OperatorAssignmentResponse(
            assignment.Id,
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
        await using var coverageTransaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({EffectiveOperatorAssignmentQueries.AllocationLock})", cancellationToken);

        var assignment = await dbContext.OperatorAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.OperatorUserId == operatorId, cancellationToken);

        if (assignment is null)
        {
            throw new NotFoundException(UserErrorCodes.NotFound, "Operator assignment not found.");
        }

        if (assignment.RevokedAt.HasValue)
        {
            throw new ConflictException(UserErrorCodes.AssignmentConflict, "Operator assignment was already revoked.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        assignment.RevokedAt = now;
        assignment.Reason = reason ?? "Assignment revoked by administrator";

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorAdminId,
            Action = "OperatorAssignment.Revoked",
            EntityType = "OperatorAssignment",
            EntityId = assignment.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            Details = $"Revoked scope assignment {assignment.Id} for operator {operatorId}. Reason: {reason}",
            OccurredAt = now
        };

        dbContext.AuditLogs.Add(auditLog);
        await InvalidateCoverageForAssignmentChangeAsync(actorAdminId, operatorId, assignment.LockerId, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (coverageTransaction != null) await coverageTransaction.CommitAsync(cancellationToken);
    }

    private async Task InvalidateCoverageForAssignmentChangeAsync(Guid adminId, Guid operatorId, Guid lockerId,
        DateTimeOffset now, CancellationToken ct)
    {
        var affected = await dbContext.TemporaryOperatorAssignments
            .Where(x => (x.Status == TemporaryAssignmentStatus.Approved || x.Status == TemporaryAssignmentStatus.Active) &&
                (x.OperatorUserId == operatorId || x.LockerId == lockerId || x.AnchorLockerId == lockerId)).ToListAsync(ct);
        if (affected.Count == 0) return;
        var admins = await dbContext.Users.Where(x => x.Role == UserRole.Administrator && x.Status == UserStatus.Active)
            .Select(x => x.Id).ToListAsync(ct);
        foreach (var coverage in affected)
        {
            coverage.Status = TemporaryAssignmentStatus.NeedsApproval;
            coverage.ActivatedAt = null;
            coverage.StatusReason = "Original assignments changed. Review anchors and whole-interval load before reapproval.";
            dbContext.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = adminId,
                Action = "OperatorReplacement.ReapprovalRequired",
                EntityType = nameof(TemporaryOperatorAssignment),
                EntityId = coverage.Id,
                Details = coverage.StatusReason,
                Result = AuditLogResult.Succeeded,
                OccurredAt = now
            });
            foreach (Guid recipient in admins.Concat(coverage.OperatorUserId is { } substitute ? new[] { substitute } : Array.Empty<Guid>()).Distinct())
                dbContext.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = recipient,
                    Type = "OperatorReplacement",
                    Title = "Coverage requires reapproval",
                    Message = $"Locker {coverage.LockerId}; {coverage.StatusReason}",
                    Channel = NotificationChannel.InApp,
                    DeliveryStatus = NotificationDeliveryStatus.Sent,
                    CreatedAt = now,
                    SentAt = now
                });
        }
    }

    private static UserDetailResponse MapToDetail(User user)
    {
        var assignments = user.OperatorAssignments
            .OrderByDescending(a => a.AssignedAt)
            .Select(a => new OperatorAssignmentResponse(
                a.Id,
                a.LockerId,
                a.Locker?.Code,
                a.AssignedAt,
                a.RevokedAt,
                a.Reason
            )).ToList();

        return new UserDetailResponse(
            user.Id,
            user.ResidentProfile?.FullName ?? user.PhoneNumber ?? user.Email ?? "User",
            user.PhoneNumber,
            user.Email,
            user.Role,
            user.Status,
            user.MustChangePassword,
            user.ResidentProfile?.DateOfBirth,
            user.ResidentProfile?.AvatarUrl,
            user.ResidentProfile?.DeliveryApprovalMode,
            user.ResidentProfile?.FaceRecognitionEnabled ?? false,
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
