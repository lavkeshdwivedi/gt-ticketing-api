using Ticketing.Application.Abstractions;
using Ticketing.Application.Common;

namespace Ticketing.Application.Events;

public sealed record DeleteEventCommand(Guid Id);

public sealed class DeleteEventCommandHandler(IEventRepository events, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task HandleAsync(DeleteEventCommand command, CancellationToken cancellationToken)
    {
        var @event = await events.GetForUpdateAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("Event", command.Id);

        // If a purchase commits between load and save, it bumps the event's rowversion and this
        // save fails with a concurrency conflict instead of deleting an event that has sales.
        @event.Delete(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
