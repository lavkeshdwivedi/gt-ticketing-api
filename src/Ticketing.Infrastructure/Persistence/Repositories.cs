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
