using Ticketing.Application.Abstractions;
using Ticketing.Application.Common;

namespace Ticketing.Application.Events;

public sealed record CancelEventCommand(Guid Id);

public sealed class CancelEventCommandHandler(IEventRepository events, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<EventDto> HandleAsync(CancelEventCommand command, CancellationToken cancellationToken)
    {
        var @event = await events.GetForUpdateAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("Event", command.Id);

        @event.Cancel(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return @event.ToDto(events.GetVersion(@event));
    }
}
