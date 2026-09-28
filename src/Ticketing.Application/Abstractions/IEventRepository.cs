using Ticketing.Domain.Events;

namespace Ticketing.Application.Abstractions;

public interface IEventRepository
{
    /// <summary>Loads the full aggregate (with tiers) with change tracking, ready to be modified and saved.</summary>
    Task<Event?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Loads the full aggregate without change tracking. In-memory changes are never persisted.</summary>
    Task<Event?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken);

    void Add(Event @event);

    /// <summary>Opaque concurrency token of a loaded aggregate, used as the HTTP ETag.</summary>
    string GetVersion(Event @event);
}
