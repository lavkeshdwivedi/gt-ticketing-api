using Ticketing.Application.Common;

namespace Ticketing.Application.Events;

/// <summary>
/// Query side for events. Implementations project straight from storage into DTOs and never
/// materialise aggregates: reads do not need domain behaviour (see ADR 0003).
/// </summary>
public interface IEventReadModel
{
    Task<EventDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<EventSummaryDto>> ListAsync(ListEventsQuery query, CancellationToken cancellationToken);

    Task<AvailabilityDto?> GetAvailabilityAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);
}
