using Ticketing.Domain.Orders;

namespace Ticketing.Application.Orders;

public sealed record TicketDto(Guid Id, string Code);

public sealed record OrderDto(
    Guid Id,
    Guid EventId,
    Guid TierId,
    string TierName,
    int Quantity,
    decimal UnitPrice,
    decimal Total,
    string Currency,
    string CustomerName,
    string CustomerEmail,
    DateTimeOffset PurchasedAt,
    IReadOnlyList<TicketDto> Tickets);

/// <summary>Outcome of a purchase. <see cref="Replayed"/> is true when an earlier order was returned for the same idempotency key.</summary>
public sealed record PurchaseResult(OrderDto Order, bool Replayed);

internal static class OrderMapping
{
    public static OrderDto ToDto(this TicketOrder order) => new(
        order.Id,
        order.EventId,
        order.PricingTierId,
        order.TierName,
        order.Quantity,
        order.UnitPrice.Amount,
        order.Total.Amount,
        order.Total.Currency,
        order.CustomerName,
        order.CustomerEmail,
        order.PurchasedAt,
        order.Tickets.Select(t => new TicketDto(t.Id, t.Code)).ToList());
}
