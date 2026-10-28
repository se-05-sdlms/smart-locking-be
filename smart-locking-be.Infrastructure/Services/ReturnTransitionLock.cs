using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

internal static class ReturnTransitionLock
{
    // Serialize cancellation, online guest access and completion across API/worker instances.
    public static async Task<IDbContextTransaction?> AcquireAsync(
        ApplicationDbContext dbContext, Guid returnId, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()) return null;
        var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"ReturnRequest\" WHERE \"Id\" = {returnId} FOR UPDATE", cancellationToken);
            var tracked = dbContext.ReturnRequests.Local.FirstOrDefault(item => item.Id == returnId);
            if (tracked is not null)
            {
                await dbContext.Entry(tracked).ReloadAsync(cancellationToken);
                dbContext.Entry(tracked).Reference(item => item.Cancellation).IsLoaded = false;
                await dbContext.Entry(tracked).Reference(item => item.Cancellation).LoadAsync(cancellationToken);
            }
            return transaction;
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            throw;
        }
    }
}
