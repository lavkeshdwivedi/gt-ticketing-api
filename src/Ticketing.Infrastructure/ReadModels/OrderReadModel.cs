using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Orders;
using Ticketing.Domain.Orders;
using Ticketing.Infrastructure.Persistence;

namespace Ticketing.Infrastructure.ReadModels;

internal sealed class OrderReadModel(TicketingDbContext db) : IOrderReadModel
{
    /// <summary>Hard cap until the endpoint grows pagination (noted in the README).</summary>
    private const int MaxOrdersPerUser = 100;

    private static readonly Expression<Func<TicketOrder, OrderDto>> ToDto = o => new OrderDto(
        o.Id,
        o.EventId,
        o.PricingTierId,
        o.TierName,
        o.Quantity,
        o.UnitPrice.Amount,
        o.UnitPrice.Amount * o.Quantity,
        o.UnitPrice.Currency,
        o.CustomerName,
        o.CustomerEmail,
        o.PurchasedAt,
        o.Tickets.OrderBy(t => t.Id).Select(t => new TicketDto(t.Id, t.Code)).ToList());

    public Task<OrderDto?> GetAsync(Guid id, string? ownerId, CancellationToken cancellationToken)
    {
        var orders = db.TicketOrders.AsNoTracking().Where(o => o.Id == id);
        if (ownerId is not null)
        {
            orders = orders.Where(o => o.PurchasedBy == ownerId);
        }

        return orders.Select(ToDto).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrderDto>> ListForUserAsync(string ownerId, CancellationToken cancellationToken) =>
        await db.TicketOrders
            .AsNoTracking()
            .Where(o => o.PurchasedBy == ownerId)
            .OrderByDescending(o => o.PurchasedAt)
            .Take(MaxOrdersPerUser)
            .Select(ToDto)
            .ToListAsync(cancellationToken);
}
