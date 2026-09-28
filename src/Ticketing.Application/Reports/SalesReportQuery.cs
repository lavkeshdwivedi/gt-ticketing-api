using FluentValidation;
using Ticketing.Application.Common;

namespace Ticketing.Application.Reports;

/// <summary>Events whose start time falls in [From, To). Both bounds are optional.</summary>
public sealed record SalesReportQuery(DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 20)
{
    public PageRequest Paging => new(Page, PageSize);
}

public sealed class SalesReportQueryValidator : AbstractValidator<SalesReportQuery>
{
    public SalesReportQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(q => q.To).GreaterThan(q => q.From).When(q => q.From is not null && q.To is not null)
            .WithMessage("'to' must be later than 'from'.");
    }
}

public interface ISalesReportReadModel
{
    Task<EventSalesSummaryDto?> GetEventSummaryAsync(Guid eventId, CancellationToken cancellationToken);

    Task<SalesReportDto> GetReportAsync(SalesReportQuery query, DateTimeOffset now, CancellationToken cancellationToken);
}
