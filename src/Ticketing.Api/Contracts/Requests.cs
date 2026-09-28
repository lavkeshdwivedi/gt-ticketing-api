using Ticketing.Application.Common;
using Ticketing.Application.Events;
using Ticketing.Application.Orders;

namespace Ticketing.Api.Contracts;

// Wire contracts are kept separate from application commands so the HTTP shape can evolve
// (versioning, renames) without touching use cases. Missing JSON fields arrive as null/default
// and are reported by the validators as 400s rather than as null reference exceptions.

public sealed record PricingTierRequest(Guid? Id, string Name, decimal Price, int Capacity);

public sealed record CreateEventRequest(
    string Name,
    string? Description,
    string Venue,
    DateTimeOffset? StartsAt,
    string Currency,
    int TotalCapacity,
    IReadOnlyList<PricingTierRequest>? Tiers)
{
    public CreateEventCommand ToCommand() =>
        new(Name, Description, Venue, StartsAt ?? default, Currency, TotalCapacity, Tiers.ToInputs());
}

/// <summary>Full replacement (PUT). Existing tiers are referenced by id; tiers without an id are created.</summary>
public sealed record UpdateEventRequest(
    string Name,
    string? Description,
    string Venue,
    DateTimeOffset? StartsAt,
    string Currency,
    int TotalCapacity,
    IReadOnlyList<PricingTierRequest>? Tiers)
{
    public UpdateEventCommand ToCommand(Guid id, string? expectedVersion) =>
        new(id, Name, Description, Venue, StartsAt ?? default, Currency, TotalCapacity, Tiers.ToInputs(), expectedVersion);
}

public sealed record PurchaseTicketsRequest(Guid TierId, int Quantity, string CustomerName, string CustomerEmail)
{
    public PurchaseTicketsCommand ToCommand(Guid eventId, string? idempotencyKey, Caller caller) =>
        new(eventId, TierId, Quantity, CustomerName, CustomerEmail, idempotencyKey, caller);
}

internal static class RequestMapping
{
    public static IReadOnlyList<PricingTierInput> ToInputs(this IReadOnlyList<PricingTierRequest>? tiers) =>
        tiers?.Select(t => new PricingTierInput(t.Id, t.Name, t.Price, t.Capacity)).ToList() ?? [];
}
