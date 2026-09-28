using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Common;
using Ticketing.Domain.Common;
using Ticketing.Domain.Events;
using Ticketing.Domain.Orders;

namespace Ticketing.Application.Orders;

public sealed record PurchaseTicketsCommand(
    Guid EventId,
    Guid TierId,
    int Quantity,
    string CustomerName,
    string CustomerEmail,
    string? IdempotencyKey,
    Caller Caller);

public sealed class PurchaseTicketsCommandValidator : AbstractValidator<PurchaseTicketsCommand>
{
    public PurchaseTicketsCommandValidator()
    {
        RuleFor(c => c.TierId).NotEmpty();
        RuleFor(c => c.Quantity).InclusiveBetween(1, TicketOrder.MaxTicketsPerOrder);
        RuleFor(c => c.CustomerName).NotEmpty().MaximumLength(TicketOrder.CustomerNameMaxLength);
        RuleFor(c => c.CustomerEmail).NotEmpty().MaximumLength(TicketOrder.CustomerEmailMaxLength).EmailAddress();
        RuleFor(c => c.IdempotencyKey)
            .MaximumLength(TicketOrder.IdempotencyKeyMaxLength)
            .Matches(@"^[A-Za-z0-9_\-:.]+$")
            .WithMessage("Idempotency-Key may only contain letters, digits and the characters - _ : .")
            .When(c => c.IdempotencyKey is not null);
    }
}

/// <summary>
/// Purchase flow:
/// 1. Replay the existing order if this caller already used the idempotency key.
/// 2. Run every business rule in memory against a snapshot of the event (fast, precise errors).
/// 3. In one transaction, insert the order, then atomically reserve the seats in the database.
///    Step 3 is the real guard against overselling; the snapshot in step 2 can be stale, so the
///    reservation also re-checks that the tier still has the price the order was placed at.
/// </summary>
public sealed class PurchaseTicketsCommandHandler(
    IValidator<PurchaseTicketsCommand> validator,
    IEventRepository events,
    ITicketOrderRepository orders,
    ITicketInventory inventory,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<PurchaseResult> HandleAsync(PurchaseTicketsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var fingerprint = Fingerprint(command);
        if (command.IdempotencyKey is not null)
        {
            var previous = await orders.FindByIdempotencyKeyAsync(command.Caller.UserId, command.IdempotencyKey, cancellationToken);
            if (previous is not null)
            {
                return Replay(previous, fingerprint);
            }
        }

        var now = clock.GetUtcNow();
        var @event = await events.GetReadOnlyAsync(command.EventId, cancellationToken)
            ?? throw new NotFoundException("Event", command.EventId);

        var tier = @event.ReserveTickets(command.TierId, command.Quantity, now);
        var order = TicketOrder.Place(
            @event,
            tier,
            command.Quantity,
            command.CustomerName,
            command.CustomerEmail,
            command.Caller.UserId,
            command.IdempotencyKey,
            fingerprint,
            now);

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    // Insert first, while holding no contended lock. A duplicate idempotency key fails
                    // here, before any inventory is touched.
                    orders.Add(order);
                    await unitOfWork.SaveChangesAsync(ct);

                    // Then the short, contended step. Any failure rolls back the order insert too.
                    var outcome = await inventory.TryReserveAsync(@event.Id, tier.Id, command.Quantity, order.UnitPrice, now, ct);
                    return outcome switch
                    {
                        ReservationOutcome.Reserved => order.Id,
                        ReservationOutcome.EventNotOnSale => throw new DomainConflictException(
                            "event.not_on_sale", "The event is no longer on sale (cancelled, removed or started)."),
                        ReservationOutcome.PriceChanged => throw new DomainConflictException(
                            "tier.price_changed",
                            $"The price of tier '{tier.Name}' changed while this purchase was in progress. Review the new price and try again."),
                        _ => throw PricingTier.Errors.InsufficientInventory(tier.Name),
                    };
                },
                cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException) when (command.IdempotencyKey is not null)
        {
            // A concurrent request with the same key committed first. Our transaction rolled back,
            // releasing the seats, so answer with the order that won.
            var winner = await orders.FindByIdempotencyKeyAsync(command.Caller.UserId, command.IdempotencyKey, cancellationToken)
                ?? throw new ConcurrencyConflictException();
            return Replay(winner, fingerprint);
        }

        return new PurchaseResult(order.ToDto(), Replayed: false);
    }

    private static PurchaseResult Replay(TicketOrder previous, string fingerprint) =>
        previous.RequestFingerprint == fingerprint
            ? new PurchaseResult(previous.ToDto(), Replayed: true)
            : throw new IdempotencyKeyReusedException();

    internal static string Fingerprint(PurchaseTicketsCommand command)
    {
        var canonical = string.Join(
            '|',
            command.EventId.ToString("N"),
            command.TierId.ToString("N"),
            command.Quantity.ToString(CultureInfo.InvariantCulture),
            command.CustomerName.Trim(),
            command.CustomerEmail.Trim().ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
