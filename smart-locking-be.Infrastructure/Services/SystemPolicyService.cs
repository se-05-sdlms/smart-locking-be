using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class SystemPolicyService : ISystemPolicyService
{
    private readonly ApplicationDbContext dbContext;
    private readonly TimeProvider timeProvider;

    public SystemPolicyService(ApplicationDbContext dbContext)
        : this(dbContext, TimeProvider.System)
    {
    }

    public SystemPolicyService(ApplicationDbContext dbContext, TimeProvider? timeProvider)
    {
        this.dbContext = dbContext;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SystemPolicyResponse> GetActivePolicyAsync(CancellationToken cancellationToken = default)
    {
        var policy = await dbContext.SystemPolicies
            .AsNoTracking()
            .Include(p => p.NotificationRules)
            .SingleOrDefaultAsync(p => p.IsActive, cancellationToken);

        if (policy is null)
        {
            throw new KeyNotFoundException("Không tìm thấy chính sách hệ thống đang có hiệu lực.");
        }

        return MapToResponse(policy);
    }

    public async Task<SystemPolicyResponse> UpdatePolicyAsync(
        Guid adminId,
        UpdateSystemPolicyRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentException("Dữ liệu cập nhật chính sách không được để trống.", nameof(request));
        }

        var adminUser = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == adminId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Không xác định được danh tính quản trị viên.");

        if (adminUser.Role != UserRole.Administrator)
        {
            throw new UnauthorizedAccessException("Chỉ Quản trị viên (Administrator) mới có quyền cập nhật chính sách hệ thống.");
        }

        ValidateRequest(request);

        var currentPolicy = await dbContext.SystemPolicies
            .Include(p => p.NotificationRules)
            .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);

        if (currentPolicy is not null && request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != currentPolicy.Version)
        {
            throw new InvalidOperationException($"Phiên bản chính sách không còn mới nhất (hiện tại: v{currentPolicy.Version}, yêu cầu: v{request.ExpectedVersion.Value}). Vui lòng tải lại trang.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        int nextVersion = currentPolicy is not null ? currentPolicy.Version + 1 : 1;

        if (currentPolicy is not null)
        {
            currentPolicy.IsActive = false;
            currentPolicy.EffectiveTo = now;
        }

        var newPolicy = new SystemPolicy
        {
            Id = Guid.NewGuid(),
            Version = nextVersion,
            DefaultApprovalMode = request.DefaultApprovalMode,
            GuestSessionTimeoutMinutes = request.GuestSessionTimeoutMinutes,
            ManualApprovalTimeoutMinutes = request.ManualApprovalTimeoutMinutes,
            CompartmentReservationMinutes = request.CompartmentReservationMinutes,
            OverdueStartAfterHours = request.OverdueStartAfterHours,
            OverdueFeePerHour = request.OverdueFeePerHour,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            MaxStorageHours = request.MaxStorageHours,
            ClearanceEligibilityAfterHours = request.ClearanceEligibilityAfterHours,
            ClearanceNoticeBeforeHours = request.ClearanceNoticeBeforeHours,
            OtpMaxAttempts = request.OtpMaxAttempts,
            OtpLockoutMinutes = request.OtpLockoutMinutes,
            EnableOtp = request.EnableOtp,
            EnableRemoteUnlock = request.EnableRemoteUnlock,
            EnableFaceRecognition = request.EnableFaceRecognition,
            EffectiveFrom = now,
            EffectiveTo = null,
            IsActive = true,
            CreatedByUserId = adminId,
            CreatedAt = now
        };

        dbContext.SystemPolicies.Add(newPolicy);

        if (request.NotificationRules is not null)
        {
            foreach (var ruleReq in request.NotificationRules)
            {
                var rule = new NotificationRule
                {
                    Id = Guid.NewGuid(),
                    SystemPolicyId = newPolicy.Id,
                    EventType = ruleReq.EventType.Trim(),
                    Channel = ruleReq.Channel,
                    LeadTimeMinutes = ruleReq.LeadTimeMinutes,
                    IsEnabled = ruleReq.IsEnabled
                };

                newPolicy.NotificationRules.Add(rule);
                dbContext.NotificationRules.Add(rule);
            }
        }
        else if (currentPolicy is not null)
        {
            foreach (var oldRule in currentPolicy.NotificationRules)
            {
                var rule = new NotificationRule
                {
                    Id = Guid.NewGuid(),
                    SystemPolicyId = newPolicy.Id,
                    EventType = oldRule.EventType,
                    Channel = oldRule.Channel,
                    LeadTimeMinutes = oldRule.LeadTimeMinutes,
                    IsEnabled = oldRule.IsEnabled
                };

                newPolicy.NotificationRules.Add(rule);
                dbContext.NotificationRules.Add(rule);
            }
        }

        string details = currentPolicy is not null
            ? $"Cập nhật chính sách hệ thống từ phiên bản v{currentPolicy.Version} lên v{newPolicy.Version}. " +
              $"ApprovalMode: {newPolicy.DefaultApprovalMode}, " +
              $"OverdueStart: {newPolicy.OverdueStartAfterHours}h, " +
              $"OverdueFee: {newPolicy.OverdueFeePerHour} {newPolicy.Currency}/h, " +
              $"MaxStorage: {newPolicy.MaxStorageHours}h, " +
              $"Clearance: {newPolicy.ClearanceEligibilityAfterHours}h, " +
              $"UnlockMethods: [OTP={newPolicy.EnableOtp}, Remote={newPolicy.EnableRemoteUnlock}, Face={newPolicy.EnableFaceRecognition}]"
            : $"Khởi tạo chính sách hệ thống phiên bản v{newPolicy.Version}.";

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = adminId,
            Action = "UpdateSystemPolicy",
            EntityType = "SystemPolicy",
            EntityId = newPolicy.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            Details = details,
            OccurredAt = now
        };

        dbContext.AuditLogs.Add(auditLog);

        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToResponse(newPolicy);
    }

    private static void ValidateRequest(UpdateSystemPolicyRequest request)
    {
        if (request.GuestSessionTimeoutMinutes <= 0)
        {
            throw new ArgumentException("Thời gian chờ phiên khách (GuestSessionTimeoutMinutes) phải lớn hơn 0.", nameof(request.GuestSessionTimeoutMinutes));
        }

        if (request.ManualApprovalTimeoutMinutes <= 0)
        {
            throw new ArgumentException("Thời gian chờ duyệt thủ công (ManualApprovalTimeoutMinutes) phải lớn hơn 0.", nameof(request.ManualApprovalTimeoutMinutes));
        }

        if (request.CompartmentReservationMinutes <= 0)
        {
            throw new ArgumentException("Thời gian giữ chỗ ngăn tủ (CompartmentReservationMinutes) phải lớn hơn 0.", nameof(request.CompartmentReservationMinutes));
        }

        if (request.OverdueStartAfterHours < 0)
        {
            throw new ArgumentException("Thời gian bắt đầu tính quá hạn (OverdueStartAfterHours) không được nhỏ hơn 0.", nameof(request.OverdueStartAfterHours));
        }

        if (request.OverdueFeePerHour < 0)
        {
            throw new ArgumentException("Mức phí quá hạn mỗi giờ (OverdueFeePerHour) không được nhỏ hơn 0.", nameof(request.OverdueFeePerHour));
        }

        if (string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Trim().Length != 3)
        {
            throw new ArgumentException("Mã tiền tệ (Currency) phải gồm đúng 3 ký tự (ví dụ: VND).", nameof(request.Currency));
        }

        if (request.MaxStorageHours <= 0)
        {
            throw new ArgumentException("Thời gian lưu trữ tối đa (MaxStorageHours) phải lớn hơn 0.", nameof(request.MaxStorageHours));
        }

        if (request.ClearanceEligibilityAfterHours < 0)
        {
            throw new ArgumentException("Thời gian đủ điều kiện tịch thu (ClearanceEligibilityAfterHours) không được nhỏ hơn 0.", nameof(request.ClearanceEligibilityAfterHours));
        }

        if (request.ClearanceNoticeBeforeHours < 0)
        {
            throw new ArgumentException("Thời gian thông báo trước khi tịch thu (ClearanceNoticeBeforeHours) không được nhỏ hơn 0.", nameof(request.ClearanceNoticeBeforeHours));
        }

        if (request.OtpMaxAttempts <= 0)
        {
            throw new ArgumentException("Số lần nhập OTP tối đa (OtpMaxAttempts) phải lớn hơn 0.", nameof(request.OtpMaxAttempts));
        }

        if (request.OtpLockoutMinutes <= 0)
        {
            throw new ArgumentException("Thời gian khóa OTP tạm thời (OtpLockoutMinutes) phải lớn hơn 0.", nameof(request.OtpLockoutMinutes));
        }

        // Cross-field validations
        if (request.MaxStorageHours <= request.OverdueStartAfterHours)
        {
            throw new ArgumentException("Thời gian lưu trữ tối đa (MaxStorageHours) phải lớn hơn thời gian bắt đầu tính quá hạn (OverdueStartAfterHours).");
        }

        if (request.ClearanceEligibilityAfterHours < request.OverdueStartAfterHours)
        {
            throw new ArgumentException("Thời gian đủ điều kiện tịch thu (ClearanceEligibilityAfterHours) phải lớn hơn hoặc bằng thời gian bắt đầu tính quá hạn (OverdueStartAfterHours).");
        }

        if (request.ClearanceEligibilityAfterHours > request.MaxStorageHours)
        {
            throw new ArgumentException("Thời gian đủ điều kiện tịch thu (ClearanceEligibilityAfterHours) không được vượt quá thời gian lưu trữ tối đa (MaxStorageHours).");
        }

        if (request.ClearanceNoticeBeforeHours >= request.ClearanceEligibilityAfterHours && request.ClearanceEligibilityAfterHours > 0)
        {
            throw new ArgumentException("Thời gian thông báo trước khi tịch thu (ClearanceNoticeBeforeHours) phải nhỏ hơn thời gian đủ điều kiện tịch thu (ClearanceEligibilityAfterHours).");
        }

        if (!request.EnableOtp && !request.EnableRemoteUnlock && !request.EnableFaceRecognition)
        {
            throw new ArgumentException("Phải kích hoạt ít nhất một phương thức mở ngăn tủ (OTP, Mở từ xa hoặc Nhận diện khuôn mặt).");
        }

        if (request.NotificationRules is not null)
        {
            var seenKeys = new HashSet<(string, NotificationChannel)>();
            foreach (var rule in request.NotificationRules)
            {
                if (string.IsNullOrWhiteSpace(rule.EventType))
                {
                    throw new ArgumentException("EventType của quy tắc thông báo không được để trống.");
                }

                if (rule.LeadTimeMinutes.HasValue && rule.LeadTimeMinutes.Value < 0)
                {
                    throw new ArgumentException("LeadTimeMinutes của quy tắc thông báo không được nhỏ hơn 0.");
                }

                var key = (rule.EventType.Trim().ToUpperInvariant(), rule.Channel);
                if (!seenKeys.Add(key))
                {
                    throw new ArgumentException($"Quy tắc thông báo cho sự kiện '{rule.EventType}' và kênh '{rule.Channel}' bị trùng lặp.");
                }
            }
        }
    }

    private static SystemPolicyResponse MapToResponse(SystemPolicy policy)
    {
        var rules = policy.NotificationRules
            .Select(r => new NotificationRuleResponse(
                r.Id,
                r.EventType,
                r.Channel,
                r.LeadTimeMinutes,
                r.IsEnabled))
            .ToList();

        return new SystemPolicyResponse(
            policy.Id,
            policy.Version,
            policy.DefaultApprovalMode,
            policy.GuestSessionTimeoutMinutes,
            policy.ManualApprovalTimeoutMinutes,
            policy.CompartmentReservationMinutes,
            policy.OverdueStartAfterHours,
            policy.OverdueFeePerHour,
            policy.Currency,
            policy.MaxStorageHours,
            policy.ClearanceEligibilityAfterHours,
            policy.ClearanceNoticeBeforeHours,
            policy.OtpMaxAttempts,
            policy.OtpLockoutMinutes,
            policy.EnableOtp,
            policy.EnableRemoteUnlock,
            policy.EnableFaceRecognition,
            policy.EffectiveFrom,
            policy.EffectiveTo,
            policy.IsActive,
            policy.CreatedByUserId,
            policy.CreatedAt,
            rules
        );
    }
}
