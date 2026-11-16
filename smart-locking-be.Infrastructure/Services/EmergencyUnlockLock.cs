using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

internal static class EmergencyUnlockLock
{
    public static async Task<IDbContextTransaction?> AcquireAsync(
        ApplicationDbContext db, Guid compartmentId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            // Reject overlapping commands immediately rather than queueing another OPEN
            // behind a command that has already completed by the time its waiter runs.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"LockerCompartment\" WHERE \"Id\" = {compartmentId} FOR UPDATE NOWAIT", ct);
            return transaction;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            if (transaction is not null) await transaction.DisposeAsync();
            throw new ConflictException(LockerErrorCodes.UnlockInProgress, "An unlock command for this compartment is already in progress.");
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            throw;
        }
    }
}
