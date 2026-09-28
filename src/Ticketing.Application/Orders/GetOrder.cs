using Ticketing.Application.Common;

namespace Ticketing.Application.Orders;

public sealed class GetOrderQueryHandler(IOrderReadModel orders)
{
    /// <summary>
    /// Someone else's order returns 404 rather than 403, so order ids cannot be probed for existence.
    /// </summary>
    public async Task<OrderDto> HandleAsync(Guid orderId, Caller caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var owned = await orders.GetAsync(orderId, cancellationToken);
        if (owned is null || (!caller.IsAdmin && owned.PurchasedBy != caller.UserId))
        {
            throw new NotFoundException("Order", orderId);
        }

        return owned.Order;
    }
}

public sealed class ListMyOrdersQueryHandler(IOrderReadModel orders)
{
    public Task<IReadOnlyList<OrderDto>> HandleAsync(Caller caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return orders.ListForUserAsync(caller.UserId, cancellationToken);
    }
}
