using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Events;
using Ticketing.Domain.Orders;
using Ticketing.Infrastructure.Persistence.Configurations;

namespace Ticketing.Infrastructure.Persistence;

internal sealed class EventRepository(TicketingDbContext db) : IEventRepository
{
    public Task<Event?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Events.Include(e => e.Tiers).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<Event?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken) =>
        db.Events.AsNoTracking().Include(e => e.Tiers).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public void Add(Event @event) => db.Events.Add(@event);

    public string GetVersion(Event @event) =>
        RowVersion.Encode(db.Entry(@event).Property<byte[]>(EventConfiguration.Version).CurrentValue);
}

internal sealed class TicketOrderRepository(TicketingDbContext db) : ITicketOrderRepository
{
    public Task<TicketOrder?> FindByIdempotencyKeyAsync(string purchasedBy, string idempotencyKey, CancellationToken cancellationToken) =>
        db.TicketOrders
            .AsNoTracking()
            .Include(o => o.Tickets)
            .SingleOrDefaultAsync(o => o.PurchasedBy == purchasedBy && o.IdempotencyKey == idempotencyKey, cancellationToken);

    public void Add(TicketOrder order) => db.TicketOrders.Add(order);
}

/// <summary>
/// Seat reservation as two guarded, set-based UPDATEs inside the caller's transaction.
/// No read-modify-write cycle, so there is no window in which two buyers see the same free seat.
/// </summary>
internal sealed class TicketInventory(TicketingDbContext db) : ITicketInventory
{
    public async Task<ReservationOutcome> TryReserveAsync(
        Guid eventId, Guid tierId, int quantity, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // 1. Lock the event row and bump its rowversion. Zero rows means it was cancelled, deleted
        //    (global query filter) or started since we read it. Holding this lock also serialises the
        //    purchase with admin edits, which then fail their concurrency check instead of racing us.
        var eventRows = await db.Events
            .Where(e => e.Id == eventId && e.Status == EventStatus.Scheduled && e.StartsAt > now)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => EF.Property<DateTimeOffset?>(e, EventConfiguration.LastSoldAt), now),
                cancellationToken);
        if (eventRows == 0)
        {
            return ReservationOutcome.EventNotOnSale;
        }

        // 2. Conditional increment: only succeeds if the seats are still there at write time.
        var tierRows = await db.PricingTiers
            .Where(t => t.Id == tierId && t.EventId == eventId && t.Sold + quantity <= t.Capacity)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Sold, t => t.Sold + quantity), cancellationToken);

        return tierRows == 1 ? ReservationOutcome.Reserved : ReservationOutcome.InsufficientInventory;
    }
}
