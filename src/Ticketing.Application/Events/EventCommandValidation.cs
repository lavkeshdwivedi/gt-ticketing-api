using FluentValidation;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events;

public sealed record PricingTierInput(Guid? Id, string Name, decimal Price, int Capacity)
{
    public PricingTierDefinition ToDefinition() => new(Id, Name, Price, Capacity);
}

/// <summary>Field-level rules shared by create and update. Cross-field business rules live in the domain.</summary>
internal static class EventCommandValidation
{
    public static void AddEventRules<T>(
        this AbstractValidator<T> validator,
        Func<T, string> name,
        Func<T, string?> description,
        Func<T, string> venue,
        Func<T, DateTimeOffset> startsAt,
        Func<T, string> currency,
        Func<T, int> totalCapacity,
        Func<T, IReadOnlyList<PricingTierInput>> tiers)
    {
        validator.RuleFor(c => name(c)).NotEmpty().MaximumLength(Event.NameMaxLength).OverridePropertyName("name");
        validator.RuleFor(c => description(c)).MaximumLength(Event.DescriptionMaxLength).OverridePropertyName("description");
        validator.RuleFor(c => venue(c)).NotEmpty().MaximumLength(Event.VenueMaxLength).OverridePropertyName("venue");
        validator.RuleFor(c => startsAt(c)).NotEmpty().WithMessage("'startsAt' is required.").OverridePropertyName("startsAt");
        validator.RuleFor(c => currency(c)).NotEmpty().Matches("^[A-Za-z]{3}$")
            .WithMessage("'currency' must be a three-letter ISO 4217 code.").OverridePropertyName("currency");
        validator.RuleFor(c => totalCapacity(c)).GreaterThan(0).OverridePropertyName("totalCapacity");
        validator.RuleFor(c => tiers(c)).NotNull().Must(t => t is { Count: > 0 and <= Event.MaxTiers })
            .WithMessage($"'tiers' must contain between 1 and {Event.MaxTiers} items.").OverridePropertyName("tiers");
        validator.RuleForEach(c => tiers(c)).ChildRules(tier =>
        {
            tier.RuleFor(t => t.Name).NotEmpty().MaximumLength(PricingTier.NameMaxLength);
            tier.RuleFor(t => t.Price).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
            tier.RuleFor(t => t.Capacity).GreaterThan(0);
        }).OverridePropertyName("tiers");
    }
}
