using Ticketing.Domain.Events;

namespace Ticketing.Application.Reports;

public sealed record TierSalesDto(
    Guid TierId,
    string Name,
    decimal CurrentPrice,
    int Capacity,
    int TicketsSold,
    int TicketsRemaining,
    decimal GrossRevenue);

public sealed record EventSalesSummaryDto(
    Guid EventId,
    string Name,
    DateTimeOffset StartsAt,
    EventStatus Status,
    string Currency,
    int TotalCapacity,
    int TicketsSold,
    int TicketsRemaining,
    decimal SellThroughPercent,
    int OrderCount,
    decimal GrossRevenue,
    IReadOnlyList<TierSalesDto> Tiers);

/// <summary>Totals are grouped per currency: amounts in different currencies are never summed together.</summary>
public sealed record CurrencyTotalDto(string Currency, int EventCount, int TicketsSold, int OrderCount, decimal GrossRevenue);

public sealed record SalesReportDto(
    DateTimeOffset? From,
    DateTimeOffset? To,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CurrencyTotalDto> Totals,
    IReadOnlyList<EventSalesSummaryDto> Events,
    int Page,
    int PageSize,
    int TotalEvents);
