namespace smart_locking_be.Application.Interfaces.Services;

/// <summary>
/// Ranh giới giao dịch đặt lại ngăn. Implementation rollback khi dispose nếu chưa commit;
/// chi tiết isolation level và transaction của EF nằm ở Infrastructure.
/// </summary>
public interface ICompartmentResetTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
