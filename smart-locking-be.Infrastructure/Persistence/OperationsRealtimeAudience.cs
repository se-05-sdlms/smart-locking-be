using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Infrastructure.Persistence;

/// <summary>Adapter EF chỉ đọc; dùng chung truy vấn phân công thực tế của dev, gồm lịch vắng/thay ca.</summary>
public sealed class OperationsRealtimeAudience(ApplicationDbContext db) : IOperationsRealtimeAudience
{
    public Task<string?> GetDeviceIdentifierAsync(Guid lockerId, CancellationToken cancellationToken = default) =>
        db.Lockers.AsNoTracking().Where(x => x.Id == lockerId)
            .Select(x => (string?)x.DeviceIdentifier).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> GetRecipientIdsAsync(Guid lockerId, DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        var operators = db.EffectiveOperatorAssignments(at).Where(x => x.LockerId == lockerId).Select(x => x.OperatorUserId);
        var admins = db.Users.Where(x => x.Role == UserRole.Administrator && x.Status == UserStatus.Active).Select(x => x.Id);
        // Union tránh gửi trùng; không dùng phân công gốc để bỏ qua người đang thay ca/vắng mặt.
        return await operators.Union(admins).ToListAsync(cancellationToken);
    }
}
