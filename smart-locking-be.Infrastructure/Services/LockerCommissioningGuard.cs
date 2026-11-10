using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

internal static class LockerCommissioningGuard
{
    public static async Task<IDbContextTransaction?> LockAsync(ApplicationDbContext db, Guid lockerId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational()) return null;
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Locker\" WHERE \"Id\" = {lockerId} FOR UPDATE", cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public static async Task EnsureActivationAllowedAsync(ApplicationDbContext db, Guid lockerId, CancellationToken cancellationToken)
    {
        if (await db.Set<LockerCommissioning>().AnyAsync(item => item.LockerId == lockerId &&
            item.Stage != LockerCommissioningStage.HandedOver, cancellationToken))
            throw new ConflictException(LockerErrorCodes.NotOperational, "Complete commissioning handover before activating this locker.");
    }

    public static async Task EnsureMappingEditableAsync(ApplicationDbContext db, Guid lockerId, CancellationToken cancellationToken)
    {
        if (await db.Set<LockerCommissioning>().AnyAsync(item => item.LockerId == lockerId &&
            item.Stage == LockerCommissioningStage.HandedOver, cancellationToken))
            throw new ConflictException(LockerErrorCodes.NotOperational, "Hardware changes after handover require a separate recommissioning procedure.");
    }
}
