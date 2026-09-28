namespace Ticketing.Application.Orders;

public sealed record OwnedOrder(OrderDto Order, string PurchasedBy);

public interface IOrderReadModel
{
    Task<OwnedOrder?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderDto>> ListForUserAsync(string purchasedBy, CancellationToken cancellationToken);
}
