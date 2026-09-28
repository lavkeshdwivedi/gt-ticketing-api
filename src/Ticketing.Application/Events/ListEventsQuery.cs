using FluentValidation;
using Ticketing.Application.Common;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events;

public sealed record ListEventsQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Search = null,
    EventStatus? Status = null,
    int Page = 1,
    int PageSize = 20)
{
    public PageRequest Paging => new(Page, PageSize);
}

public sealed class ListEventsQueryValidator : AbstractValidator<ListEventsQuery>
{
    public ListEventsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).When(q => q.From is not null && q.To is not null)
            .WithMessage("'to' must not be earlier than 'from'.");
    }
}
