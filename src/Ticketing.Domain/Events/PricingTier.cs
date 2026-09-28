using Ticketing.Domain.Common;

namespace Ticketing.Domain.Events;

/// <summary>
/// A price point with its own seat allocation, e.g. "VIP: 50 seats at 150.00 USD".
/// Owned by <see cref="Event"/>; all changes go through the aggregate root.
/// </summary>
public sealed class PricingTier
{
    public const int NameMaxLength = 100;

    private PricingTier()
    {
        Name = null!;
        Price = null!;
    }

    private PricingTier(Guid id, Guid eventId, string name, Money price, int capacity)
    {
        Id = id;
        EventId = eventId;
        Name = name;
        Price = price;
        Capacity = capacity;
    }

    public Guid Id { get; private set; }

    public Guid EventId { get; private set; }

    public string Name { get; private set; }

    public Money Price { get; private set; }

    public int Capacity { get; private set; }

    /// <summary>
    /// Tickets sold so far. In production this counter is only ever advanced by an atomic,
    /// guarded UPDATE in the database (see ADR 0001); <see cref="Reserve"/> models the same
    /// rule in memory so it can be checked early and unit tested.
    /// </summary>
    public int Sold { get; private set; }

    public int Available => Capacity - Sold;

    internal static PricingTier Create(Guid eventId, PricingTierDefinition definition, string currency)
    {
        var tier = new PricingTier(
            SequentialGuid.NewGuid(),
            eventId,
            Guard.Required(definition.Name, "tier.name", NameMaxLength),
            Money.Of(definition.Price, currency),
            ValidCapacity(definition.Capacity));
        return tier;
    }

    internal void Update(PricingTierDefinition definition, string currency)
    {
        var capacity = ValidCapacity(definition.Capacity);
        if (capacity < Sold)
        {
            throw new DomainConflictException(
                "tier.capacity_below_sold",
                $"Tier '{Name}' has already sold {Sold} tickets; capacity cannot be reduced to {capacity}.");
        }

        Name = Guard.Required(definition.Name, "tier.name", NameMaxLength);
        Price = Money.Of(definition.Price, currency);
        Capacity = capacity;
    }

    internal void Reserve(int quantity)
    {
        if (quantity > Available)
        {
            throw Errors.InsufficientInventory(Name, Available);
        }

        Sold += quantity;
    }

    private static int ValidCapacity(int capacity) =>
        capacity > 0
            ? capacity
            : throw new BusinessRuleViolationException("tier.capacity_invalid", "Tier capacity must be greater than zero.");

    public static class Errors
    {
        public static DomainConflictException InsufficientInventory(string tierName, int? available = null) =>
            new("tickets.sold_out", available switch
            {
                0 => $"Tier '{tierName}' is sold out.",
                > 0 => $"Only {available} tickets remain in tier '{tierName}'.",
                _ => $"Not enough tickets remain in tier '{tierName}'.",
            });
    }
}
