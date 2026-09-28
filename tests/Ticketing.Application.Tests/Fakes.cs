using Microsoft.Extensions.Time.Testing;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Tests;

/// <summary>
/// Runs the transactional delegate inline and models commit/rollback: saves made inside a
/// transaction only count as committed if the whole delegate completes.
/// </summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    private int _pendingSaves;
    private bool _inTransaction;

    public int CommittedSaves { get; private set; }

    public Exception? ThrowOnSave { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (ThrowOnSave is not null)
        {
            throw ThrowOnSave;
        }

        if (_inTransaction)
        {
            _pendingSaves++;
        }
        else
        {
            CommittedSaves++;
        }

        return Task.CompletedTask;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        _inTransaction = true;
        _pendingSaves = 0;
        try
        {
            var result = await operation(cancellationToken);
            CommittedSaves += _pendingSaves;
            return result;
        }
        finally
        {
            _inTransaction = false;
            _pendingSaves = 0;
        }
    }
}

internal static class Clock
{
    public static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    public static FakeTimeProvider Fixed() => new(Now);
}

internal static class Events
{
    public static Event Concert() => Event.Create(
        "Symphony Night",
        null,
        "Main Hall",
        Clock.Now.AddDays(30),
        "USD",
        100,
        [new PricingTierDefinition(null, "VIP", 150m, 10), new PricingTierDefinition(null, "General Admission", 50m, 90)],
        Clock.Now);
}
