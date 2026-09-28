using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Api.Auth;
using Ticketing.Application.Orders;

namespace Ticketing.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/orders")]
public sealed class OrdersController : ControllerBase
{
    public const string GetOrderRoute = nameof(GetOrderRoute);

    /// <summary>The caller's own orders, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OrderDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<OrderDto>> Mine([FromServices] ListMyOrdersQueryHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(User.ToCaller(), cancellationToken);

    /// <summary>An order with its tickets. Only visible to its purchaser or an events admin; otherwise 404.</summary>
    [HttpGet("{id:guid}", Name = GetOrderRoute)]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<OrderDto> Get(Guid id, [FromServices] GetOrderQueryHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(id, User.ToCaller(), cancellationToken);
}
