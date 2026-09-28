using FluentValidation;
using Ticketing.Application.Common;

namespace Ticketing.Application.Reports;

public sealed class GetEventSalesSummaryQueryHandler(ISalesReportReadModel readModel)
{
    public async Task<EventSalesSummaryDto> HandleAsync(Guid eventId, CancellationToken cancellationToken) =>
        await readModel.GetEventSummaryAsync(eventId, cancellationToken) ?? throw new NotFoundException("Event", eventId);
}

public sealed class GetSalesReportQueryHandler(
    IValidator<SalesReportQuery> validator,
    ISalesReportReadModel readModel,
    TimeProvider clock)
{
    public async Task<SalesReportDto> HandleAsync(SalesReportQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        return await readModel.GetReportAsync(query, clock.GetUtcNow(), cancellationToken);
    }
}
