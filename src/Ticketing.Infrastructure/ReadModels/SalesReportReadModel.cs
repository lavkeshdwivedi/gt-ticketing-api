using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Reports;
using Ticketing.Domain.Events;
using Ticketing.Infrastructure.Persistence;

namespace Ticketing.Infrastructure.ReadModels;

/// <summary>
/// Revenue and tickets sold come from order snapshots (what customers actually paid); remaining
/// seats come from the tier inventory counters. The two are kept consistent by the purchase transaction,
/// and the integration tests assert that they reconcile.
/// </summary>
internal sealed class SalesReportReadModel(TicketingDbContext db) : ISalesReportReadModel
{
    public async Task<EventSalesSummaryDto?> GetEventSummaryAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var summaries = await BuildSummariesAsync(db.Events.Where(e => e.Id == eventId), cancellationToken);
        return summaries.SingleOrDefault();
    }

    public async Task<SalesReportDto> GetReportAsync(SalesReportQuery query, DateTimeOffset now, CancellationToken cancellationToken)
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

        var totalEvents = await events.CountAsync(cancellationToken);
        var page = events
            .OrderBy(e => e.StartsAt)
            .ThenBy(e => e.Id)
            .Skip(query.Paging.Skip)
            .Take(query.PageSize);
        var summaries = await BuildSummariesAsync(page, cancellationToken);

        // Totals cover the whole date range, not just this page, and are grouped by currency:
        // adding USD to EUR would produce a number that means nothing.
        var salesByCurrency = await db.TicketOrders
            .Where(o => events.Any(e => e.Id == o.EventId))
            .GroupBy(o => o.UnitPrice.Currency)
            .Select(g => new
            {
                Currency = g.Key,
                Orders = g.Count(),
                Tickets = g.Sum(o => o.Quantity),
                Revenue = g.Sum(o => o.UnitPrice.Amount * o.Quantity),
            })
            .ToListAsync(cancellationToken);
        var eventsByCurrency = await events
            .GroupBy(e => e.Currency)
            .Select(g => new { Currency = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var totals = eventsByCurrency
            .OrderBy(c => c.Currency, StringComparer.Ordinal)
            .Select(c =>
            {
                var sales = salesByCurrency.SingleOrDefault(s => s.Currency == c.Currency);
                return new CurrencyTotalDto(c.Currency, c.Count, sales?.Tickets ?? 0, sales?.Orders ?? 0, sales?.Revenue ?? 0m);
            })
            .ToList();

        return new SalesReportDto(query.From, query.To, now, totals, summaries, query.Page, query.PageSize, totalEvents);
    }

    private async Task<List<EventSalesSummaryDto>> BuildSummariesAsync(IQueryable<Event> events, CancellationToken cancellationToken)
    {
        var rows = await events
            .AsNoTracking()
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.StartsAt,
                e.Status,
                e.Currency,
                e.TotalCapacity,
                Tiers = e.Tiers
                    .OrderBy(t => t.Id)
                    .Select(t => new { t.Id, t.Name, Price = t.Price.Amount, t.Capacity, t.Sold })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var eventIds = rows.Select(r => r.Id).ToList();
        var sales = await db.TicketOrders
            .Where(o => eventIds.Contains(o.EventId))
            .GroupBy(o => o.PricingTierId)
            .Select(g => new
            {
                TierId = g.Key,
                Orders = g.Count(),
                Tickets = g.Sum(o => o.Quantity),
                Revenue = g.Sum(o => o.UnitPrice.Amount * o.Quantity),
            })
            .ToDictionaryAsync(s => s.TierId, cancellationToken);

        return rows.Select(e =>
        {
            var tiers = e.Tiers.Select(t =>
            {
                sales.TryGetValue(t.Id, out var tierSales);
                return new TierSalesDto(
                    t.Id, t.Name, t.Price, t.Capacity, tierSales?.Tickets ?? 0, t.Capacity - t.Sold, tierSales?.Revenue ?? 0m);
            }).ToList();

            var sold = tiers.Sum(t => t.TicketsSold);
            var orderCount = e.Tiers.Sum(t => sales.TryGetValue(t.Id, out var s) ? s.Orders : 0);

            return new EventSalesSummaryDto(
                e.Id,
                e.Name,
                e.StartsAt,
                e.Status,
                e.Currency,
                e.TotalCapacity,
                sold,
                tiers.Sum(t => t.TicketsRemaining),
                Math.Round(sold * 100m / e.TotalCapacity, 1),
                orderCount,
                tiers.Sum(t => t.GrossRevenue),
                tiers);
        }).ToList();
    }
}
