using Ticketing.Domain.Orders;

namespace Ticketing.Application.Abstractions;

public interface ITicketOrderRepository
{
    Task<TicketOrder?> FindByIdempotencyKeyAsync(string purchasedBy, string idempotencyKey, CancellationToken cancellationToken);

    void Add(TicketOrder order);
}
