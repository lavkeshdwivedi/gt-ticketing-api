using FluentValidation;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events;

public sealed record CreateEventCommand(
    string Name,
    string? Description,
    string Venue,
    DateTimeOffset StartsAt,
    string Currency,
    int TotalCapacity,
    IReadOnlyList<PricingTierInput> Tiers);

public sealed class CreateEventCommandValidator : AbstractValidator<CreateEventCommand>
{
    public CreateEventCommandValidator()
    {
        this.AddEventRules(c => c.Name, c => c.Description, c => c.Venue, c => c.Currency, c => c.TotalCapacity, c => c.Tiers);
        RuleForEach(c => c.Tiers).Must(t => t.Id is null)
            .WithMessage("Tier ids cannot be supplied when creating an event.").OverridePropertyName("tiers");
    }
}

public sealed class CreateEventCommandHandler(
    IValidator<CreateEventCommand> validator,
    IEventRepository events,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<EventDto> HandleAsync(CreateEventCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var @event = Event.Create(
            command.Name,
            command.Description,
            command.Venue,
            command.StartsAt,
            command.Currency,
            command.TotalCapacity,
            command.Tiers.Select(t => t.ToDefinition()).ToList(),
            clock.GetUtcNow());

        events.Add(@event);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return @event.ToDto(events.GetVersion(@event));
    }
}
