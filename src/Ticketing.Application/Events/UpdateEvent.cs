using FluentValidation;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Common;

namespace Ticketing.Application.Events;

/// <summary>Full replacement of an event's editable state (PUT semantics).</summary>
/// <param name="ExpectedVersion">Value of the client's If-Match header, if any.</param>
public sealed record UpdateEventCommand(
    Guid Id,
    string Name,
    string? Description,
    string Venue,
    DateTimeOffset StartsAt,
    string Currency,
    int TotalCapacity,
    IReadOnlyList<PricingTierInput> Tiers,
    string? ExpectedVersion);

public sealed class UpdateEventCommandValidator : AbstractValidator<UpdateEventCommand>
{
    public UpdateEventCommandValidator() =>
        this.AddEventRules(c => c.Name, c => c.Description, c => c.Venue, c => c.StartsAt, c => c.Currency, c => c.TotalCapacity, c => c.Tiers);
}

public sealed class UpdateEventCommandHandler(
    IValidator<UpdateEventCommand> validator,
    IEventRepository events,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<EventDto> HandleAsync(UpdateEventCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var @event = await events.GetForUpdateAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("Event", command.Id);

        // Fail fast on a stale If-Match. The rowversion check at save time closes the remaining race.
        if (command.ExpectedVersion is not null && command.ExpectedVersion != events.GetVersion(@event))
        {
            throw new PreconditionFailedException();
        }

        @event.Update(
            command.Name,
            command.Description,
            command.Venue,
            command.StartsAt,
            command.Currency,
            command.TotalCapacity,
            command.Tiers.Select(t => t.ToDefinition()).ToList(),
            clock.GetUtcNow());

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException) when (command.ExpectedVersion is not null)
        {
            throw new PreconditionFailedException();
        }

        return @event.ToDto(events.GetVersion(@event));
    }
}
