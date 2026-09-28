using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Events;
using Ticketing.Domain.Orders;
using Ticketing.Infrastructure.Persistence.Configurations;

namespace Ticketing.Infrastructure.Persistence;

internal sealed class EventRepository(TicketingDbContext db) : IEventRepository
{
    public async Task<Event?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Loading an event for update must run inside a transaction.");
        }

        // Take the event row lock before reading, the same lock every purchase takes first
        // (TicketInventory). Sales for this event wait until the admin change commits, so the
        // sold counts the domain checks against (delete, capacity, tier removal) cannot move
        // underneath it, and a sale never makes the admin's write fail.
        await db.Database.ExecuteSqlAsync(
            $"SELECT TOP (1) 1 FROM Events WITH (UPDLOCK, ROWLOCK) WHERE Id = {id}", cancellationToken);

        return await db.Events.Include(e => e.Tiers).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public Task<Event?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken) =>
        db.Events.AsNoTracking().Include(e => e.Tiers).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public void Add(Event @event) => db.Events.Add(@event);

    public string GetVersion(Event @event) =>
        EventRevision.Encode(db.Entry(@event).Property<int>(EventConfiguration.Revision).CurrentValue);
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
