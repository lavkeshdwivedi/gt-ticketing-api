using FluentValidation;
using NSubstitute;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Common;
using Ticketing.Application.Orders;
using Ticketing.Domain.Common;
using Ticketing.Domain.Events;
using Ticketing.Domain.Orders;

namespace Ticketing.Application.Tests;

public sealed class PurchaseTicketsCommandHandlerTests
{
    private static readonly Caller Buyer = new("user-1", IsAdmin: false);

    private readonly IEventRepository _events = Substitute.For<IEventRepository>();
    private readonly ITicketOrderRepository _orders = Substitute.For<ITicketOrderRepository>();
    private readonly ITicketInventory _inventory = Substitute.For<ITicketInventory>();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly Event _concert = Events.Concert();

    public PurchaseTicketsCommandHandlerTests()
    {
        _events.GetReadOnlyAsync(_concert.Id, Arg.Any<CancellationToken>()).Returns(_concert);
        _inventory.TryReserveAsync(default, default, default, default, default)
            .ReturnsForAnyArgs(ReservationOutcome.Reserved);
    }

    private PurchaseTicketsCommandHandler Handler() =>
        new(new PurchaseTicketsCommandValidator(), _events, _orders, _inventory, _unitOfWork, Clock.Fixed());

    private PurchaseTicketsCommand Command(int quantity = 2, string? key = null, string email = "ada@example.com") =>
        new(_concert.Id, _concert.Tiers[0].Id, quantity, "Ada Lovelace", email, key, Buyer);

    [Fact]
    public async Task Successful_purchase_reserves_inventory_and_persists_order()
    {
        var result = await Handler().HandleAsync(Command(quantity: 2), CancellationToken.None);

        result.Replayed.ShouldBeFalse();
        result.Order.Quantity.ShouldBe(2);
        result.Order.Total.ShouldBe(300m);
        result.Order.Currency.ShouldBe("USD");
        result.Order.Tickets.Count.ShouldBe(2);
        await _inventory.Received(1).TryReserveAsync(_concert.Id, _concert.Tiers[0].Id, 2, Clock.Now, Arg.Any<CancellationToken>());
        _orders.Received(1).Add(Arg.Is<TicketOrder>(o => o.PurchasedBy == "user-1"));
        _unitOfWork.CommittedSaves.ShouldBe(1);
    }

    [Fact]
    public async Task Sold_out_at_write_time_returns_conflict_and_rolls_back_the_order()
    {
        _inventory.TryReserveAsync(default, default, default, default, default)
            .ReturnsForAnyArgs(ReservationOutcome.InsufficientInventory);

        var ex = await Should.ThrowAsync<DomainConflictException>(() => Handler().HandleAsync(Command(), CancellationToken.None));

        ex.Code.ShouldBe("tickets.sold_out");
        _unitOfWork.CommittedSaves.ShouldBe(0);
    }

    [Fact]
    public async Task Duplicate_key_collision_is_detected_before_touching_inventory()
    {
        _orders.FindByIdempotencyKeyAsync("user-1", "abc-123", Arg.Any<CancellationToken>())
            .Returns(null, TicketOrder.Place(_concert, _concert.Tiers[0], 2, "Ada Lovelace", "ada@example.com", "user-1", "abc-123",
                PurchaseTicketsCommandHandler.Fingerprint(Command(key: "abc-123")), Clock.Now));
        _unitOfWork.ThrowOnSave = new DuplicateIdempotencyKeyException();

        await Handler().HandleAsync(Command(key: "abc-123"), CancellationToken.None);

        await _inventory.DidNotReceiveWithAnyArgs().TryReserveAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Event_cancelled_between_read_and_write_returns_conflict()
    {
        _inventory.TryReserveAsync(default, default, default, default, default)
            .ReturnsForAnyArgs(ReservationOutcome.EventNotOnSale);

        var ex = await Should.ThrowAsync<DomainConflictException>(() => Handler().HandleAsync(Command(), CancellationToken.None));

        ex.Code.ShouldBe("event.not_on_sale");
    }

    [Fact]
    public async Task Business_rules_fail_fast_before_touching_inventory()
    {
        _concert.Cancel(Clock.Now);

        var ex = await Should.ThrowAsync<DomainConflictException>(() => Handler().HandleAsync(Command(), CancellationToken.None));

        ex.Code.ShouldBe("event.cancelled");
        await _inventory.DidNotReceiveWithAnyArgs().TryReserveAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Unknown_event_is_not_found()
    {
        var command = Command() with { EventId = Guid.NewGuid() };

        await Should.ThrowAsync<NotFoundException>(() => Handler().HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task Repeated_idempotency_key_replays_original_order_without_buying_again()
    {
        var original = await Handler().HandleAsync(Command(key: "abc-123"), CancellationToken.None);
        var stored = _orders.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<TicketOrder>().Single();
        _orders.FindByIdempotencyKeyAsync("user-1", "abc-123", Arg.Any<CancellationToken>()).Returns(stored);
        _inventory.ClearReceivedCalls();

        var replay = await Handler().HandleAsync(Command(key: "abc-123"), CancellationToken.None);

        replay.Replayed.ShouldBeTrue();
        replay.Order.Id.ShouldBe(original.Order.Id);
        await _inventory.DidNotReceiveWithAnyArgs().TryReserveAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Idempotency_key_reused_with_different_payload_is_rejected()
    {
        await Handler().HandleAsync(Command(quantity: 2, key: "abc-123"), CancellationToken.None);
        var stored = _orders.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<TicketOrder>().Single();
        _orders.FindByIdempotencyKeyAsync("user-1", "abc-123", Arg.Any<CancellationToken>()).Returns(stored);

        await Should.ThrowAsync<IdempotencyKeyReusedException>(
            () => Handler().HandleAsync(Command(quantity: 3, key: "abc-123"), CancellationToken.None));
    }

    [Fact]
    public async Task Losing_a_concurrent_race_on_the_same_key_returns_the_winning_order()
    {
        var winner = TicketOrder.Place(
            _concert,
            _concert.Tiers[0],
            2,
            "Ada Lovelace",
            "ada@example.com",
            "user-1",
            "abc-123",
            PurchaseTicketsCommandHandler.Fingerprint(Command(key: "abc-123")),
            Clock.Now);
        _orders.FindByIdempotencyKeyAsync("user-1", "abc-123", Arg.Any<CancellationToken>())
            .Returns(null, winner); // Not there on the first look, there after our insert collides.
        _unitOfWork.ThrowOnSave = new DuplicateIdempotencyKeyException();

        var result = await Handler().HandleAsync(Command(key: "abc-123"), CancellationToken.None);

        result.Replayed.ShouldBeTrue();
        result.Order.Id.ShouldBe(winner.Id);
    }

    [Fact]
    public void Fingerprint_ignores_email_case_and_surrounding_whitespace()
    {
        PurchaseTicketsCommandHandler.Fingerprint(Command(email: "Ada@Example.com "))
            .ShouldBe(PurchaseTicketsCommandHandler.Fingerprint(Command(email: "ada@example.com")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Invalid_quantity_is_a_validation_error(int quantity)
    {
        var ex = await Should.ThrowAsync<ValidationException>(() => Handler().HandleAsync(Command(quantity), CancellationToken.None));
        ex.Errors.ShouldContain(e => e.PropertyName == nameof(PurchaseTicketsCommand.Quantity));
    }
}
