namespace Ticketing.Domain.Events;

/// <summary>
/// Desired state of a tier when creating or updating an event.
/// <see cref="Id"/> is null for a new tier and set when updating an existing one.
/// </summary>
public sealed record PricingTierDefinition(Guid? Id, string Name, decimal Price, int Capacity);
