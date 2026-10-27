using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

internal static class ParcelTransitionLock
{
    // The same database lock protects transfer, pickup commands and pickup completion across API/worker instances.
    public static async Task<IDbContextTransaction?> AcquireAsync(
        ApplicationDbContext dbContext, Guid parcelId, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()) return null;
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Parcel\" WHERE \"Id\" = {parcelId} FOR UPDATE", cancellationToken);
            var tracked = dbContext.Parcels.Local.FirstOrDefault(item => item.Id == parcelId);
            if (tracked is not null) await dbContext.Entry(tracked).ReloadAsync(cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
