using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Common;
using Ticketing.Infrastructure.Persistence.Configurations;

namespace Ticketing.Infrastructure.Persistence;

internal sealed class UnitOfWork(TicketingDbContext db) : IUnitOfWork
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int ForeignKeyViolation = 547;

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, TicketOrderConfiguration.IdempotencyIndex))
        {
            throw new DuplicateIdempotencyKeyException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: ForeignKeyViolation })
        {
            // Something this write refers to was removed concurrently, e.g. an admin deleted an
            // unsold tier while a buyer was purchasing from it. The caller should reload and retry.
            throw new ConcurrencyConflictException(ex);
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // With EnableRetryOnFailure, user transactions must run inside the execution strategy so
        // the whole unit (not a single statement) is retried on a transient Azure SQL fault.
        var strategy = db.Database.CreateExecutionStrategy();
        var attempt = 0;
        return await strategy.ExecuteAsync(
            async ct =>
            {
                if (attempt++ > 0)
                {
                    db.ChangeTracker.Clear();
                }

                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var result = await operation(ct);
                await transaction.CommitAsync(ct);
                return result;
            },
            cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException ex, string indexName) =>
        ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sql
        && sql.Message.Contains(indexName, StringComparison.Ordinal);
}
