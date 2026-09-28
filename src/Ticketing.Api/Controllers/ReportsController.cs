using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Api.Auth;
using Ticketing.Application.Reports;

namespace Ticketing.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.ReportsRead)]
[Route("api/v1/reports")]
[Produces("application/json")]
public sealed class ReportsController : ControllerBase
{
    /// <summary>
    /// Sales across events starting in [from, to). Per-event breakdown is paged; totals cover the
    /// whole range and are grouped by currency.
    /// </summary>
    [HttpGet("sales")]
    [ProducesResponseType<SalesReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<SalesReportDto> Sales(
        [FromServices] GetSalesReportQueryHandler handler,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        handler.HandleAsync(new SalesReportQuery(from, to, page, pageSize), cancellationToken);
}
