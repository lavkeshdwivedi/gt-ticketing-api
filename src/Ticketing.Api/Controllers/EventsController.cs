using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Ticketing.Api.Auth;
using Ticketing.Api.Contracts;
using Ticketing.Api.Http;
using Ticketing.Application.Common;
using Ticketing.Application.Events;
using Ticketing.Application.Orders;
using Ticketing.Application.Reports;
using Ticketing.Domain.Events;

namespace Ticketing.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
[Produces("application/json")]
public sealed class EventsController : ControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string IdempotentReplayedHeader = "Idempotent-Replayed";

    /// <summary>List events, soonest first. Filter by start time window, status or a name/venue search.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<EventSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<EventSummaryDto>> List(
        [FromServices] ListEventsQueryHandler handler,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? search,
        [FromQuery] EventStatus? status,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        handler.HandleAsync(new ListEventsQuery(from, to, search, status, page, pageSize), cancellationToken);

    /// <summary>Get an event. Returns an ETag for conditional updates; honours If-None-Match.</summary>
    [HttpGet("{id:guid}", Name = nameof(GetEvent))]
    [AllowAnonymous]
    [ProducesResponseType<EventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvent(
        Guid id,
        [FromServices] GetEventQueryHandler handler,
        [FromHeader(Name = "If-None-Match")] string? ifNoneMatch,
        CancellationToken cancellationToken)
    {
        var dto = await handler.HandleAsync(id, cancellationToken);
        Response.Headers.ETag = ETags.Format(dto.Version);
        return ETags.IfNoneMatchHits(ifNoneMatch, dto.Version) ? StatusCode(StatusCodes.Status304NotModified) : Ok(dto);
    }

    [HttpPost]
    [Authorize(Policy = Policies.EventsManage)]
    [ProducesResponseType<EventDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        CreateEventRequest request,
        [FromServices] CreateEventCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var dto = await handler.HandleAsync(request.ToCommand(), cancellationToken);
        Response.Headers.ETag = ETags.Format(dto.Version);
        return CreatedAtRoute(nameof(GetEvent), new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Replace an event. Send If-Match with the ETag from GET to avoid overwriting someone else's change (412 if stale).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.EventsManage)]
    [ProducesResponseType<EventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateEventRequest request,
        [FromServices] UpdateEventCommandHandler handler,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        var dto = await handler.HandleAsync(request.ToCommand(id, ETags.ParseIfMatch(ifMatch)), cancellationToken);
        Response.Headers.ETag = ETags.Format(dto.Version);
        return Ok(dto);
    }

    /// <summary>Soft-delete an event. Events with sales cannot be deleted (409); cancel them instead.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.EventsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id, [FromServices] DeleteEventCommandHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new DeleteEventCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Cancel an event and stop sales. Idempotent.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.EventsManage)]
    [ProducesResponseType<EventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        Guid id, [FromServices] CancelEventCommandHandler handler, CancellationToken cancellationToken)
    {
        var dto = await handler.HandleAsync(new CancelEventCommand(id), cancellationToken);
        Response.Headers.ETag = ETags.Format(dto.Version);
        return Ok(dto);
    }

    [HttpGet("{id:guid}/availability")]
    [AllowAnonymous]
    [ProducesResponseType<AvailabilityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Availability(
        Guid id, [FromServices] GetAvailabilityQueryHandler handler, CancellationToken cancellationToken)
    {
        var dto = await handler.HandleAsync(id, cancellationToken);
        Response.Headers.CacheControl = "no-store"; // Inventory changes by the second.
        return Ok(dto);
    }

    /// <summary>
    /// Purchase tickets. Send an Idempotency-Key header so retries can never double-buy:
    /// a repeated key returns the original order with Idempotent-Replayed: true.
    /// </summary>
    [HttpPost("{id:guid}/purchases")]
    [Authorize]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Purchase(
        Guid id,
        PurchaseTicketsRequest request,
        [FromServices] PurchaseTicketsCommandHandler handler,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            request.ToCommand(id, string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(), User.ToCaller()),
            cancellationToken);

        if (result.Replayed)
        {
            Response.Headers[IdempotentReplayedHeader] = "true";
        }

        return CreatedAtRoute(OrdersController.GetOrderRoute, new { id = result.Order.Id }, result.Order);
    }

    [HttpGet("{id:guid}/sales-summary")]
    [Authorize(Policy = Policies.ReportsRead)]
    [ProducesResponseType<EventSalesSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EventSalesSummaryDto> SalesSummary(
        Guid id, [FromServices] GetEventSalesSummaryQueryHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(id, cancellationToken);
}
