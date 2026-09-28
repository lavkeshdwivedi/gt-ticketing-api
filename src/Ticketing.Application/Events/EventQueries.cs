using FluentValidation;
using Ticketing.Application.Common;

namespace Ticketing.Application.Events;

public sealed class GetEventQueryHandler(IEventReadModel readModel)
{
    public async Task<EventDto> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await readModel.GetAsync(id, cancellationToken) ?? throw new NotFoundException("Event", id);
}

public sealed class ListEventsQueryHandler(IValidator<ListEventsQuery> validator, IEventReadModel readModel)
{
    public async Task<PagedResult<EventSummaryDto>> HandleAsync(ListEventsQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        return await readModel.ListAsync(query, cancellationToken);
    }
}

public sealed class GetAvailabilityQueryHandler(IEventReadModel readModel, TimeProvider clock)
{
    public async Task<AvailabilityDto> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await readModel.GetAvailabilityAsync(id, clock.GetUtcNow(), cancellationToken)
        ?? throw new NotFoundException("Event", id);
}
