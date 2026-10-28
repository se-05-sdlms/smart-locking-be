using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class OperatorSupportService(ApplicationDbContext db, TimeProvider clock) : IOperatorSupportService
{
    public async Task<LockerSupportResponse> GetPublicAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) throw BadRequestException.For("code", "Locker code is required.");
        var locker = await db.Lockers.AsNoTracking().SingleOrDefaultAsync(x => x.Code == code.Trim(), ct)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");
        return await ResolveAsync(locker, ct);
    }

    public async Task<LockerSupportResponse> GetForUserAsync(Guid userId, string role, Guid lockerId, CancellationToken ct)
    {
        bool allowed = role == nameof(UserRole.Administrator) || role == nameof(UserRole.LockerOperator) &&
            await db.EffectiveOperatorAssignments(clock.GetUtcNow()).AnyAsync(x => x.OperatorUserId == userId && x.LockerId == lockerId, ct);
        if (role == nameof(UserRole.Resident))
            allowed = await db.ResidentProfiles.AnyAsync(x => x.UserId == userId && x.RegisteredLockerId == lockerId, ct) ||
                await db.DeliveryRequests.AnyAsync(x => x.LockerId == lockerId && x.ResidentProfile!.UserId == userId, ct) ||
                await db.ReturnRequests.AnyAsync(x => x.LockerId == lockerId && x.ResidentProfile.UserId == userId, ct) ||
                await db.Incidents.AnyAsync(x => x.LockerId == lockerId && x.ReporterUserId == userId, ct);
        if (!allowed) throw new ForbiddenException(LockerErrorCodes.AccessDenied, "Locker is outside your registered locker or transaction context.");
        var locker = await db.Lockers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == lockerId, ct)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");
        return await ResolveAsync(locker, ct);
    }

    private async Task<LockerSupportResponse> ResolveAsync(Locker locker, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        Guid? current = await db.EffectiveOperatorAssignments(now).Where(x => x.LockerId == locker.Id)
            .Select(x => (Guid?)x.OperatorUserId).SingleOrDefaultAsync(ct);
        var contact = current.HasValue ? await db.Set<OperatorSupportContact>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.OperatorUserId == current && x.IsPublic, ct) : null;
        if (contact != null && InHours(contact, now))
            return new(locker.Code, true, false, contact.DisplayName, contact.WorkPhoneNumber, Hours(contact), contact.Instructions, now);
        var fallback = await db.Set<LockerSupportSettings>().AsNoTracking().SingleOrDefaultAsync(x => x.LockerId == locker.Id, ct);
        return new(locker.Code, fallback?.FallbackPhoneNumber != null, true, fallback?.FallbackName,
            fallback?.FallbackPhoneNumber, fallback?.FallbackHours,
            fallback?.FallbackInstructions ?? "Chưa có liên hệ hỗ trợ được công bố cho tủ này. Vui lòng liên hệ ban quản lý nơi đặt tủ.", now);
    }

    public async Task<OperatorSupportContactResponse?> GetContactAsync(Guid operatorId, CancellationToken ct)
    {
        var contact = await db.Set<OperatorSupportContact>().AsNoTracking().SingleOrDefaultAsync(x => x.OperatorUserId == operatorId, ct);
        return contact == null ? null : Map(contact);
    }

    public async Task<OperatorSupportContactResponse> SaveContactAsync(Guid adminId, Guid operatorId,
        SaveOperatorSupportContactRequest request, CancellationToken ct)
    {
        string name = Text(request.DisplayName, 200, "displayName"), instructions = Text(request.Instructions, 1000, "instructions");
        string phone = Phone(request.WorkPhoneNumber) ?? throw BadRequestException.For("workPhoneNumber", "A public work phone is required.");
        if (request.UtcOffsetMinutes is < -840 or > 840 || request.WeekdaysMask is < 1 or > 127 ||
            request.StartsAtMinute is < 0 or > 1439 || request.EndsAtMinute is < 0 or > 1440 || request.StartsAtMinute == request.EndsAtMinute)
            throw BadRequestException.For("supportHours", "Configure a UTC offset, day mask and nonempty daily support interval.");
        if (!await db.Users.AnyAsync(x => x.Id == operatorId && x.Role == UserRole.LockerOperator, ct))
            throw new NotFoundException(UserErrorCodes.NotFound, "Operator not found.");
        var item = await db.Set<OperatorSupportContact>().SingleOrDefaultAsync(x => x.OperatorUserId == operatorId, ct);
        if (item == null) { item = new() { OperatorUserId = operatorId }; db.Add(item); }
        item.DisplayName = name; item.WorkPhoneNumber = phone; item.IsPublic = request.IsPublic;
        item.UtcOffsetMinutes = request.UtcOffsetMinutes; item.WeekdaysMask = request.WeekdaysMask;
        item.StartsAtMinute = request.StartsAtMinute; item.EndsAtMinute = request.EndsAtMinute;
        item.Instructions = instructions; item.UpdatedByUserId = adminId; item.UpdatedAt = clock.GetUtcNow();
        Audit(adminId, "OperatorSupportContact.Updated", operatorId);
        await db.SaveChangesAsync(ct);
        return Map(item);
    }

    public async Task<SaveLockerSupportSettingsRequest?> GetSettingsAsync(Guid lockerId, CancellationToken ct)
    {
        var item = await db.Set<LockerSupportSettings>().AsNoTracking().SingleOrDefaultAsync(x => x.LockerId == lockerId, ct);
        return item == null ? null : new(item.FallbackName, item.FallbackPhoneNumber, item.FallbackInstructions, item.FallbackHours);
    }

    public async Task<SaveLockerSupportSettingsRequest> SaveSettingsAsync(Guid adminId, Guid lockerId,
        SaveLockerSupportSettingsRequest request, CancellationToken ct)
    {
        if (!await db.Lockers.AnyAsync(x => x.Id == lockerId, ct)) throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");
        string instructions = Text(request.FallbackInstructions, 1000, "fallbackInstructions");
        string? phone = Phone(request.FallbackPhoneNumber);
        if (request.FallbackName?.Trim().Length > 200 || request.FallbackHours?.Trim().Length > 200)
            throw BadRequestException.For("fallback", "Fallback name/hours must not exceed 200 characters.");
        var item = await db.Set<LockerSupportSettings>().SingleOrDefaultAsync(x => x.LockerId == lockerId, ct);
        if (item == null) { item = new() { LockerId = lockerId }; db.Add(item); }
        item.FallbackName = NullTrim(request.FallbackName); item.FallbackPhoneNumber = phone;
        item.FallbackInstructions = instructions; item.FallbackHours = NullTrim(request.FallbackHours);
        item.UpdatedByUserId = adminId; item.UpdatedAt = clock.GetUtcNow();
        Audit(adminId, "LockerSupportSettings.Updated", lockerId);
        await db.SaveChangesAsync(ct);
        return new(item.FallbackName, item.FallbackPhoneNumber, item.FallbackInstructions, item.FallbackHours);
    }

    private static bool InHours(OperatorSupportContact x, DateTimeOffset now)
    {
        DateTimeOffset local = now.ToOffset(TimeSpan.FromMinutes(x.UtcOffsetMinutes));
        int minute = local.Hour * 60 + local.Minute;
        int day = (int)local.DayOfWeek;
        if (x.StartsAtMinute < x.EndsAtMinute)
            return (x.WeekdaysMask & 1 << day) != 0 && minute >= x.StartsAtMinute && minute < x.EndsAtMinute;
        return minute >= x.StartsAtMinute && (x.WeekdaysMask & 1 << day) != 0 ||
            minute < x.EndsAtMinute && (x.WeekdaysMask & 1 << ((day + 6) % 7)) != 0;
    }
    private static string Hours(OperatorSupportContact x) =>
        $"UTC{(x.UtcOffsetMinutes >= 0 ? "+" : "-")}{Math.Abs(x.UtcOffsetMinutes) / 60:00}:{Math.Abs(x.UtcOffsetMinutes) % 60:00}; " +
        string.Join(", ", Enumerable.Range(0, 7).Where(day => (x.WeekdaysMask & 1 << day) != 0).Select(day => new[] { "CN", "T2", "T3", "T4", "T5", "T6", "T7" }[day])) +
        $" {x.StartsAtMinute / 60:00}:{x.StartsAtMinute % 60:00}–{x.EndsAtMinute / 60:00}:{x.EndsAtMinute % 60:00}";
    private static string? Phone(string? value)
    {
        string? phone = NullTrim(value);
        if (phone != null && !Regex.IsMatch(phone, @"^\+?[0-9]{7,15}$"))
            throw BadRequestException.For("phoneNumber", "Use a public work phone with 7–15 digits and an optional leading +.");
        return phone;
    }
    private static string? NullTrim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Text(string? value, int max, string field) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
        ? value.Trim() : throw BadRequestException.For(field, $"Required, maximum {max} characters.");
    private static OperatorSupportContactResponse Map(OperatorSupportContact x) => new(x.OperatorUserId, x.DisplayName, x.WorkPhoneNumber,
        x.IsPublic, x.UtcOffsetMinutes, x.WeekdaysMask, x.StartsAtMinute, x.EndsAtMinute, x.Instructions, x.UpdatedAt);
    private void Audit(Guid adminId, string action, Guid id) => db.AuditLogs.Add(new AuditLog
    {
        Id = Guid.NewGuid(),
        ActorUserId = adminId,
        Action = action,
        EntityType = "OperatorSupport",
        EntityId = id,
        Result = AuditLogResult.Succeeded,
        OccurredAt = clock.GetUtcNow()
    });
}
