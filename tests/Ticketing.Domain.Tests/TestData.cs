using Ticketing.Domain.Events;

namespace Ticketing.Domain.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset NextMonth = Now.AddDays(30);

    public static PricingTierDefinition Tier(string name, decimal price, int capacity, Guid? id = null) =>
        new(id, name, price, capacity);

    /// <summary>A valid event: VIP 10 @ 150.00 + GA 90 @ 50.00, 100 seats, USD.</summary>
    public static Event Concert(DateTimeOffset? startsAt = null) => Event.Create(
        "Symphony Night",
        "An evening of Beethoven.",
        "Main Hall",
        startsAt ?? NextMonth,
        "usd",
        100,
        [Tier("VIP", 150m, 10), Tier("General Admission", 50m, 90)],
        Now);

    public static PricingTier TierNamed(this Event @event, string name) => @event.Tiers.Single(t => t.Name == name);

    public static List<PricingTierDefinition> CurrentTiers(this Event @event) =>
        @event.Tiers.Select(t => new PricingTierDefinition(t.Id, t.Name, t.Price.Amount, t.Capacity)).ToList();

    public static void UpdateTiers(this Event @event, IReadOnlyCollection<PricingTierDefinition> tiers, int? totalCapacity = null, string? currency = null) =>
        @event.Update(
            @event.Name,
            @event.Description,
            @event.Venue,
            @event.StartsAt,
            currency ?? @event.Currency,
            totalCapacity ?? tiers.Sum(t => t.Capacity),
            tiers,
            Now);
}
