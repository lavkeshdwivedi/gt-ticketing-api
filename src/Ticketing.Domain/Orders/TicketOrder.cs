using Ticketing.Domain.Common;
using Ticketing.Domain.Events;

namespace Ticketing.Domain.Orders;

/// <summary>
/// A completed purchase. Price and tier name are snapshotted so that later edits to the
/// event never rewrite what a customer actually paid.
/// </summary>
public sealed class TicketOrder
{
    public const int MaxTicketsPerOrder = 10;
    public const int CustomerNameMaxLength = 200;
    public const int CustomerEmailMaxLength = 254;
    public const int IdempotencyKeyMaxLength = 100;
    public const int PurchasedByMaxLength = 128;

    private readonly List<Ticket> _tickets = [];

    private TicketOrder()
    {
        TierName = null!;
        UnitPrice = null!;
        Total = null!;
        CustomerName = null!;
        CustomerEmail = null!;
        PurchasedBy = null!;
    }

    public Guid Id { get; private set; }

    public Guid EventId { get; private set; }

    public Guid PricingTierId { get; private set; }

    public string TierName { get; private set; }

    public int Quantity { get; private set; }

    public Money UnitPrice { get; private set; }

    public Money Total { get; private set; }

    public string CustomerName { get; private set; }

    public string CustomerEmail { get; private set; }

    /// <summary>Subject (user id) of the authenticated caller who placed the order. Only they, or an admin, can read it.</summary>
    public string PurchasedBy { get; private set; }

    public string? IdempotencyKey { get; private set; }

    /// <summary>Hash of the purchase request, used to reject reuse of an idempotency key with a different payload.</summary>
    public string? RequestFingerprint { get; private set; }

    public DateTimeOffset PurchasedAt { get; private set; }

    public IReadOnlyList<Ticket> Tickets => _tickets;

    /// <summary>
    /// Creates the order for a reservation already accepted by <see cref="Event.ReserveTickets"/>.
    /// </summary>
    public static TicketOrder Place(
        Event @event,
        PricingTier tier,
        int quantity,
        string customerName,
        string customerEmail,
        string purchasedBy,
        string? idempotencyKey,
        string? requestFingerprint,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(tier);
        if (tier.EventId != @event.Id)
        {
            throw new BusinessRuleViolationException("tier.unknown", $"Tier {tier.Id} does not belong to this event.");
        }

        var order = new TicketOrder
        {
            Id = SequentialGuid.NewGuid(),
            EventId = @event.Id,
            PricingTierId = tier.Id,
            TierName = tier.Name,
            Quantity = quantity,
            UnitPrice = tier.Price,
            Total = tier.Price.Multiply(quantity),
            CustomerName = Guard.Required(customerName, "customer.name", CustomerNameMaxLength),
            CustomerEmail = Guard.Required(customerEmail, "customer.email", CustomerEmailMaxLength).ToLowerInvariant(),
            PurchasedBy = Guard.Required(purchasedBy, "order.purchased_by", PurchasedByMaxLength),
            IdempotencyKey = Guard.Optional(idempotencyKey, "idempotency_key", IdempotencyKeyMaxLength),
            RequestFingerprint = requestFingerprint,
            PurchasedAt = now,
        };

        for (var i = 0; i < quantity; i++)
        {
            order._tickets.Add(Ticket.Issue(order.Id));
        }

        return order;
    }
}
