using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Common;
using Ticketing.Application.Events;
using Ticketing.Domain.Events;
using Ticketing.Infrastructure.Persistence;
using Ticketing.Infrastructure.Persistence.Configurations;

namespace Ticketing.Infrastructure.ReadModels;

internal sealed class EventReadModel(TicketingDbContext db) : IEventReadModel
{
    public async Task<EventDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.Events
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.Description,
                e.Venue,
                e.StartsAt,
                e.Currency,
                e.TotalCapacity,
                e.Status,
                Tiers = e.Tiers
                    .OrderBy(t => t.Id)
                    .Select(t => new PricingTierDto(t.Id, t.Name, t.Price.Amount, t.Capacity))
                    .ToList(),
                e.CreatedAt,
                e.UpdatedAt,
                Revision = EF.Property<int>(e, EventConfiguration.Revision),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new EventDto(
                row.Id,
                row.Name,
                row.Description,
                row.Venue,
                row.StartsAt,
                row.Currency,
                row.TotalCapacity,
                row.Status,
                row.Tiers,
                row.CreatedAt,
                row.UpdatedAt,
                EventRevision.Encode(row.Revision));
    }

    public async Task<PagedResult<EventSummaryDto>> ListAsync(ListEventsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var events = db.Events.AsNoTracking();

        if (query.From is { } from)
        {
            events = events.Where(e => e.StartsAt >= from);
        }

        if (query.To is { } to)
        {
            events = events.Where(e => e.StartsAt < to);
        }

        if (query.Status is { } status)
        {
            events = events.Where(e => e.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            events = events.Where(e => e.Name.Contains(term) || e.Venue.Contains(term));
        }

        var total = await events.CountAsync(cancellationToken);
        var items = await events
            .OrderBy(e => e.StartsAt)
            .ThenBy(e => e.Id)
            .Skip(query.Paging.Skip)
            .Take(query.PageSize)
            .Select(e => new EventSummaryDto(
                e.Id,
                e.Name,
                e.Venue,
                e.StartsAt,
                e.Status,
                e.Currency,
                e.TotalCapacity,
                e.TotalCapacity - e.Tiers.Sum(t => t.Sold)))
            .ToListAsync(cancellationToken);

        return new PagedResult<EventSummaryDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<AvailabilityDto?> GetAvailabilityAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await db.Events
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new
            {
                e.Id,
                e.Status,
                e.StartsAt,
                e.Currency,
                e.TotalCapacity,
                Tiers = e.Tiers
                    .OrderBy(t => t.Id)
                    .Select(t => new { t.Id, t.Name, Price = t.Price.Amount, t.Capacity, t.Sold })
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var tiers = row.Tiers
            .Select(t => new TierAvailabilityDto(t.Id, t.Name, t.Price, t.Capacity, t.Capacity - t.Sold, t.Sold >= t.Capacity))
            .ToList();
        var available = tiers.Sum(t => t.Available);
        var onSale = row.Status == EventStatus.Scheduled && row.StartsAt > now && available > 0;

        return new AvailabilityDto(row.Id, row.Status, onSale, row.Currency, row.TotalCapacity, available, tiers);
    }
}
