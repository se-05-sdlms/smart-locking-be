using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

internal static class ReturnPickupAttemptGuard
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public static async Task CleanupAsync(ApplicationDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        // Cleanup must finish before acquiring the request bucket: retaining unrelated
        // cleanup locks until verification completes can create cross-bucket deadlocks.
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "ReturnPickupAttempt" WHERE ("LockerId", "ClientKey") IN (
                SELECT "LockerId", "ClientKey" FROM "ReturnPickupAttempt"
                WHERE "WindowExpiresAt" <= {now} LIMIT 100 FOR UPDATE SKIP LOCKED)
            """, ct);
    }

    // Caller owns the transaction, including committing failed attempts before returning an error.
    public static async Task<ReturnPickupAttempt> AcquireAsync(ApplicationDbContext db, Guid lockerId,
        string clientKey, TimeProvider timeProvider, CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (db.Database.IsRelational())
        {
            DateTimeOffset expiresAt = now.Add(Window);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ReturnPickupAttempt" ("LockerId", "ClientKey", "FailedAttempts", "WindowExpiresAt")
                VALUES ({lockerId}, {clientKey}, 0, {expiresAt})
                ON CONFLICT ("LockerId", "ClientKey") DO NOTHING
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT 1 FROM "ReturnPickupAttempt" WHERE "LockerId" = {lockerId} AND "ClientKey" = {clientKey} FOR UPDATE
                """, ct);
        }
        else
        {
            var expired = await db.Set<ReturnPickupAttempt>().Where(item => item.WindowExpiresAt <= now).Take(100).ToListAsync(ct);
            db.RemoveRange(expired);
            await db.SaveChangesAsync(ct);
        }

        var bucket = await db.Set<ReturnPickupAttempt>().SingleOrDefaultAsync(item => item.LockerId == lockerId && item.ClientKey == clientKey, ct);
        if (bucket is null)
        {
            bucket = new ReturnPickupAttempt { LockerId = lockerId, ClientKey = clientKey, WindowExpiresAt = now.Add(Window) };
            db.Add(bucket);
        }
        else if (db.Database.IsRelational()) await db.Entry(bucket).ReloadAsync(ct);

        now = timeProvider.GetUtcNow();
        if (bucket.WindowExpiresAt <= now)
        {
            bucket.FailedAttempts = 0;
            bucket.WindowExpiresAt = now.Add(Window);
        }
        return bucket;
    }
}
