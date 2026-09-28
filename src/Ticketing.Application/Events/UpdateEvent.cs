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

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    var @event = await events.GetForUpdateAsync(command.Id, ct)
                        ?? throw new NotFoundException("Event", command.Id);

                    // Checked under the event lock, so no other admin change can slip in before the save.
                    // Sales do not change the version, so a busy on-sale does not cause spurious 412s.
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

                    await unitOfWork.SaveChangesAsync(ct);
                    return @event.ToDto(events.GetVersion(@event));
                },
                cancellationToken);
        }
        catch (ConcurrencyConflictException) when (command.ExpectedVersion is not null)
        {
            throw new PreconditionFailedException();
        }
    }
}
