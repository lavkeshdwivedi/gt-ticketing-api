using Ticketing.Domain.Common;

namespace Ticketing.Application.Abstractions;

public enum ReservationOutcome
{
    Reserved,

    /// <summary>The event was cancelled, deleted or has started since it was read.</summary>
    EventNotOnSale,

    /// <summary>The tier no longer has enough seats.</summary>
    InsufficientInventory,

    /// <summary>The tier's price changed since the buyer's snapshot was read.</summary>
    PriceChanged,
}

/// <summary>
/// Authoritative seat reservation. Implementations must be atomic with respect to concurrent
/// callers and must run inside the caller's transaction (see ADR 0001).
/// </summary>
public interface ITicketInventory
{
    /// <param name="expectedUnitPrice">
    /// The price the order was placed at. The reservation only succeeds if the tier still has this
    /// price, so a buyer is never charged a price that an admin changed mid-purchase.
    /// </param>
    Task<ReservationOutcome> TryReserveAsync(
        Guid eventId, Guid tierId, int quantity, Money expectedUnitPrice, DateTimeOffset now, CancellationToken cancellationToken);
}
