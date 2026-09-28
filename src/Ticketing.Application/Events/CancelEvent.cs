using Ticketing.Application.Abstractions;
using Ticketing.Application.Common;

namespace Ticketing.Application.Events;

public sealed record CancelEventCommand(Guid Id);

public sealed class CancelEventCommandHandler(IEventRepository events, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<EventDto> HandleAsync(CancelEventCommand command, CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var @event = await events.GetForUpdateAsync(command.Id, ct)
                    ?? throw new NotFoundException("Event", command.Id);

                @event.Cancel(clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(ct);

                return @event.ToDto(events.GetVersion(@event));
            },
            cancellationToken);
    }
}
