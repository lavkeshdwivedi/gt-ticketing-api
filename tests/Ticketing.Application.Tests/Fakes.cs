using Microsoft.Extensions.Time.Testing;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Tests;

/// <summary>Runs the transactional delegate inline and lets a test inject a failure at save time.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Exception? ThrowOnSave { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (ThrowOnSave is not null)
        {
            throw ThrowOnSave;
        }

        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        operation(cancellationToken);
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
