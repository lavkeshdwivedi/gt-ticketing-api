using Ticketing.Domain.Events;

namespace Ticketing.Application.Abstractions;

public interface IEventRepository
{
    /// <summary>
    /// Loads the full aggregate (with tiers) with change tracking, ready to be modified and saved.
    /// Must run inside <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>: it locks the event
    /// against concurrent sales until the transaction ends.
    /// </summary>
    Task<Event?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Loads the full aggregate without change tracking. In-memory changes are never persisted.</summary>
    Task<Event?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken);

    void Add(Event @event);

    /// <summary>Opaque version of the event's editable state, used as the HTTP ETag. Sales do not change it.</summary>
    string GetVersion(Event @event);
}
