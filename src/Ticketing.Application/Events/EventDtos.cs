using Ticketing.Domain.Events;

namespace Ticketing.Application.Events;

public sealed record PricingTierDto(Guid Id, string Name, decimal Price, int Capacity);

public sealed record EventDto(
    Guid Id,
    string Name,
    string? Description,
    string Venue,
    DateTimeOffset StartsAt,
    string Currency,
    int TotalCapacity,
    EventStatus Status,
    IReadOnlyList<PricingTierDto> Tiers,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Version);

public sealed record EventSummaryDto(
    Guid Id,
    string Name,
    string Venue,
    DateTimeOffset StartsAt,
    EventStatus Status,
    string Currency,
    int TotalCapacity,
    int TicketsAvailable);

public sealed record TierAvailabilityDto(Guid TierId, string Name, decimal Price, int Capacity, int Available, bool IsSoldOut);

public sealed record AvailabilityDto(
    Guid EventId,
    EventStatus Status,
    bool IsOnSale,
    string Currency,
    int TotalCapacity,
    int Available,
    IReadOnlyList<TierAvailabilityDto> Tiers);

internal static class EventMapping
{
    public static EventDto ToDto(this Event @event, string version) => new(
        @event.Id,
        @event.Name,
        @event.Description,
        @event.Venue,
        @event.StartsAt,
        @event.Currency,
        @event.TotalCapacity,
        @event.Status,
        @event.Tiers.Select(t => new PricingTierDto(t.Id, t.Name, t.Price.Amount, t.Capacity)).ToList(),
        @event.CreatedAt,
        @event.UpdatedAt,
        version);
}
