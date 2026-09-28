namespace Ticketing.Application.Abstractions;

public enum ReservationOutcome
{
    Reserved,

    /// <summary>The event was cancelled, deleted or has started since it was read.</summary>
    EventNotOnSale,

    /// <summary>The tier no longer has enough seats.</summary>
    InsufficientInventory,
}

/// <summary>
/// Authoritative seat reservation. Implementations must be atomic with respect to concurrent
/// callers and must run inside the caller's transaction (see ADR 0001).
/// </summary>
public interface ITicketInventory
{
    Task<ReservationOutcome> TryReserveAsync(
        Guid eventId, Guid tierId, int quantity, DateTimeOffset now, CancellationToken cancellationToken);
}
