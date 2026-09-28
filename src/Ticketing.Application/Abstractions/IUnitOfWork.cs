namespace Ticketing.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>
    /// Persists tracked changes. Throws <see cref="Common.ConcurrencyConflictException"/> when the
    /// aggregate was changed by someone else since it was loaded.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="operation"/> in a single database transaction. The operation may be
    /// re-executed on transient failures, so it must be safe to run more than once.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
