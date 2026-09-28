using Ticketing.Domain.Common;

namespace Ticketing.Domain.Events;

/// <summary>
/// Aggregate root for a ticketed event. Owns its pricing tiers and guards the invariants that
/// span them, most importantly that tier allocations add up exactly to the total capacity.
/// </summary>
public sealed class Event
{
    public const int NameMaxLength = 200;
    public const int VenueMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MaxTiers = 10;

    private readonly List<PricingTier> _tiers = [];

    private Event()
    {
        Name = null!;
        Venue = null!;
        Currency = null!;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public string Venue { get; private set; }

    /// <summary>The instant the event starts, with the venue's UTC offset preserved.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    public int TotalCapacity { get; private set; }

    public string Currency { get; private set; }

    public EventStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyList<PricingTier> Tiers => _tiers;

    public int TicketsSold => _tiers.Sum(t => t.Sold);

    public int TicketsAvailable => TotalCapacity - TicketsSold;

    public static Event Create(
        string name,
        string? description,
        string venue,
        DateTimeOffset startsAt,
        string currency,
        int totalCapacity,
        IReadOnlyCollection<PricingTierDefinition> tiers,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        EnsureInFuture(startsAt, now);

        var @event = new Event
        {
            Id = SequentialGuid.NewGuid(),
            Status = EventStatus.Scheduled,
            Currency = Money.NormalizeCurrency(currency),
            CreatedAt = now,
            UpdatedAt = now,
        };
        @event.SetDetails(name, description, venue, startsAt);

        EnsureTierShape(tiers, totalCapacity);
        if (tiers.Any(t => t.Id is not null))
        {
            throw new BusinessRuleViolationException("tier.id_not_allowed", "New events cannot reference existing tier ids.");
        }

        @event._tiers.AddRange(tiers.Select(t => PricingTier.Create(@event.Id, t, @event.Currency)));
        @event.TotalCapacity = totalCapacity;
        return @event;
    }

    /// <summary>
    /// Replaces the editable state of the event. Tiers are matched by id: known ids are updated,
    /// definitions without an id are added, and omitted tiers are removed if nothing was sold.
    /// </summary>
    public void Update(
        string name,
        string? description,
        string venue,
        DateTimeOffset startsAt,
        string currency,
        int totalCapacity,
        IReadOnlyCollection<PricingTierDefinition> tiers,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        EnsureModifiable(now);
        if (startsAt != StartsAt)
        {
            EnsureInFuture(startsAt, now);
        }

        var normalizedCurrency = Money.NormalizeCurrency(currency);
        if (normalizedCurrency != Currency && TicketsSold > 0)
        {
            throw new DomainConflictException(
                "event.currency_locked", "Currency cannot change after tickets have been sold.");
        }

        EnsureTierShape(tiers, totalCapacity);

        var unknownIds = tiers
            .Where(t => t.Id is not null && _tiers.All(existing => existing.Id != t.Id))
            .Select(t => t.Id)
            .ToList();
        if (unknownIds.Count > 0)
        {
            throw new BusinessRuleViolationException(
                "tier.unknown", $"Tier(s) {string.Join(", ", unknownIds)} do not belong to this event.");
        }

        var removed = _tiers.Where(existing => tiers.All(t => t.Id != existing.Id)).ToList();
        var removedWithSales = removed.FirstOrDefault(t => t.Sold > 0);
        if (removedWithSales is not null)
        {
            throw new DomainConflictException(
                "tier.has_sales", $"Tier '{removedWithSales.Name}' has sold tickets and cannot be removed.");
        }

        SetDetails(name, description, venue, startsAt);
        Currency = normalizedCurrency;

        foreach (var tier in removed)
        {
            _tiers.Remove(tier);
        }

        foreach (var definition in tiers)
        {
            if (definition.Id is { } id)
            {
                _tiers.Single(t => t.Id == id).Update(definition, Currency);
            }
            else
            {
                _tiers.Add(PricingTier.Create(Id, definition, Currency));
            }
        }

        TotalCapacity = totalCapacity;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == EventStatus.Cancelled)
        {
            return; // Cancelling twice is a no-op so clients can safely retry.
        }

        if (StartsAt <= now)
        {
            throw new DomainConflictException("event.already_started", "An event that has started cannot be cancelled.");
        }

        Status = EventStatus.Cancelled;
        UpdatedAt = now;
    }

    /// <summary>
    /// Soft deletes the event. Events with sales are financial records: they must be cancelled
    /// (and refunded) rather than made to disappear.
    /// </summary>
    public void Delete(DateTimeOffset now)
    {
        if (TicketsSold > 0)
        {
            throw new DomainConflictException(
                "event.has_sales", "Events with sold tickets cannot be deleted. Cancel the event instead.");
        }

        IsDeleted = true;
        DeletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Applies every purchase rule in memory and returns the tier being bought from.
    /// The persistence layer then applies the same reservation atomically, which is what
    /// actually protects against concurrent buyers.
    /// </summary>
    public PricingTier ReserveTickets(Guid tierId, int quantity, DateTimeOffset now)
    {
        if (Status == EventStatus.Cancelled)
        {
            throw new DomainConflictException("event.cancelled", "Tickets cannot be purchased for a cancelled event.");
        }

        if (StartsAt <= now)
        {
            throw new DomainConflictException("event.already_started", "Ticket sales have closed because the event has started.");
        }

        if (quantity is < 1 or > Orders.TicketOrder.MaxTicketsPerOrder)
        {
            throw new BusinessRuleViolationException(
                "order.quantity_invalid",
                $"Quantity must be between 1 and {Orders.TicketOrder.MaxTicketsPerOrder}.");
        }

        var tier = _tiers.SingleOrDefault(t => t.Id == tierId)
            ?? throw new BusinessRuleViolationException("tier.unknown", $"Tier {tierId} does not belong to this event.");

        tier.Reserve(quantity);
        return tier;
    }

    private void SetDetails(string name, string? description, string venue, DateTimeOffset startsAt)
    {
        Name = Guard.Required(name, "event.name", NameMaxLength);
        Description = Guard.Optional(description, "event.description", DescriptionMaxLength);
        Venue = Guard.Required(venue, "event.venue", VenueMaxLength);
        StartsAt = startsAt;
    }

    private void EnsureModifiable(DateTimeOffset now)
    {
        if (Status == EventStatus.Cancelled)
        {
            throw new DomainConflictException("event.cancelled", "A cancelled event cannot be modified.");
        }

        if (StartsAt <= now)
        {
            throw new DomainConflictException("event.already_started", "An event that has started cannot be modified.");
        }
    }

    private static void EnsureInFuture(DateTimeOffset startsAt, DateTimeOffset now)
    {
        if (startsAt <= now)
        {
            throw new BusinessRuleViolationException("event.starts_in_past", "Event start time must be in the future.");
        }
    }

    private static void EnsureTierShape(IReadOnlyCollection<PricingTierDefinition> tiers, int totalCapacity)
    {
        if (tiers.Count is 0 or > MaxTiers)
        {
            throw new BusinessRuleViolationException(
                "event.tier_count", $"An event must have between 1 and {MaxTiers} pricing tiers.");
        }

        var duplicate = tiers
            .GroupBy(t => t.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new BusinessRuleViolationException("tier.duplicate_name", $"Tier name '{duplicate.Key}' is used more than once.");
        }

        if (tiers.Where(t => t.Id is not null).GroupBy(t => t.Id).Any(g => g.Count() > 1))
        {
            throw new BusinessRuleViolationException("tier.duplicate_id", "A tier id is listed more than once.");
        }

        if (totalCapacity <= 0)
        {
            throw new BusinessRuleViolationException("event.capacity_invalid", "Total capacity must be greater than zero.");
        }

        var allocated = tiers.Sum(t => (long)t.Capacity);
        if (allocated != totalCapacity)
        {
            throw new BusinessRuleViolationException(
                "event.capacity_mismatch",
                $"Tier capacities add up to {allocated} but total capacity is {totalCapacity}.");
        }
    }
}
