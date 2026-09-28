using Ticketing.Application.Abstractions;
using Ticketing.Application.Common;

namespace Ticketing.Application.Events;

public sealed record DeleteEventCommand(Guid Id);

public sealed class DeleteEventCommandHandler(IEventRepository events, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task HandleAsync(DeleteEventCommand command, CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // The load holds the event row lock, so no sale can commit between the domain's
                // "nothing sold" check and the delete.
                var @event = await events.GetForUpdateAsync(command.Id, ct)
                    ?? throw new NotFoundException("Event", command.Id);

                @event.Delete(clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(ct);
                return true;
            },
            cancellationToken);
    }
}
