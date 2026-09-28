namespace Ticketing.Application.Orders;

public interface IOrderReadModel
{
    /// <summary>
    /// Returns the order if it exists and, when <paramref name="ownerId"/> is set, belongs to that user.
    /// Ownership is applied in the query itself so a foreign order is indistinguishable from a missing one.
    /// </summary>
    Task<OrderDto?> GetAsync(Guid id, string? ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderDto>> ListForUserAsync(string ownerId, CancellationToken cancellationToken);
}
