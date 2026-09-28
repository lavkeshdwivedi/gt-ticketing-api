using FluentValidation;
using NSubstitute;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Common;
using Ticketing.Application.Events;
using Ticketing.Application.Orders;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Tests;

public sealed class CreateEventCommandHandlerTests
{
    private readonly IEventRepository _events = Substitute.For<IEventRepository>();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private CreateEventCommandHandler Handler() => new(new CreateEventCommandValidator(), _events, _unitOfWork, Clock.Fixed());

    private static CreateEventCommand Valid() => new(
        "Symphony Night",
        "Beethoven",
        "Main Hall",
        Clock.Now.AddDays(30),
        "USD",
        100,
        [new PricingTierInput(null, "VIP", 150m, 10), new PricingTierInput(null, "GA", 50m, 90)]);

    [Fact]
    public async Task Valid_command_adds_event_and_saves()
    {
        _events.GetVersion(Arg.Any<Event>()).Returns("0000000000000001");

        var dto = await Handler().HandleAsync(Valid(), CancellationToken.None);

        dto.Name.ShouldBe("Symphony Night");
        dto.Tiers.Count.ShouldBe(2);
        dto.Version.ShouldBe("0000000000000001");
        _events.Received(1).Add(Arg.Any<Event>());
        _unitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_shape_is_rejected_before_the_domain_runs()
    {
        var command = Valid() with { Name = "", Currency = "dollars", Tiers = [] };

        var ex = await Should.ThrowAsync<ValidationException>(() => Handler().HandleAsync(command, CancellationToken.None));

        ex.Errors.Select(e => e.PropertyName).ShouldBe(["name", "currency", "tiers"], ignoreOrder: true);
        _events.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Tier_ids_cannot_be_supplied_on_create()
    {
        var command = Valid() with { Tiers = [new PricingTierInput(Guid.NewGuid(), "GA", 10m, 100)] };

        await Should.ThrowAsync<ValidationException>(() => Handler().HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_start_time_is_a_validation_error()
    {
        var command = Valid() with { StartsAt = default };

        var ex = await Should.ThrowAsync<ValidationException>(() => Handler().HandleAsync(command, CancellationToken.None));
        ex.Errors.ShouldContain(e => e.PropertyName == "startsAt");
    }
}

public sealed class UpdateEventCommandHandlerTests
{
    private readonly IEventRepository _events = Substitute.For<IEventRepository>();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly Event _concert = Events.Concert();

    public UpdateEventCommandHandlerTests()
    {
        _events.GetForUpdateAsync(_concert.Id, Arg.Any<CancellationToken>()).Returns(_concert);
        _events.GetVersion(_concert).Returns("current");
    }

    private UpdateEventCommandHandler Handler() => new(new UpdateEventCommandValidator(), _events, _unitOfWork, Clock.Fixed());

    private UpdateEventCommand Rename(string name, string? expectedVersion) => new(
        _concert.Id,
        name,
        _concert.Description,
        _concert.Venue,
        _concert.StartsAt,
        _concert.Currency,
        _concert.TotalCapacity,
        _concert.Tiers.Select(t => new PricingTierInput(t.Id, t.Name, t.Price.Amount, t.Capacity)).ToList(),
        expectedVersion);

    [Fact]
    public async Task Matching_if_match_updates_the_event()
    {
        var dto = await Handler().HandleAsync(Rename("Encore", "current"), CancellationToken.None);

        dto.Name.ShouldBe("Encore");
        _unitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Stale_if_match_fails_before_any_change_is_made()
    {
        await Should.ThrowAsync<PreconditionFailedException>(
            () => Handler().HandleAsync(Rename("Encore", "stale"), CancellationToken.None));

        _concert.Name.ShouldBe("Symphony Night");
        _unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_write_detected_at_save_is_a_precondition_failure_when_if_match_was_sent()
    {
        _unitOfWork.ThrowOnSave = new ConcurrencyConflictException();

        await Should.ThrowAsync<PreconditionFailedException>(
            () => Handler().HandleAsync(Rename("Encore", "current"), CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_write_detected_at_save_is_a_conflict_without_if_match()
    {
        _unitOfWork.ThrowOnSave = new ConcurrencyConflictException();

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => Handler().HandleAsync(Rename("Encore", expectedVersion: null), CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_event_is_not_found()
    {
        var command = Rename("Encore", null) with { Id = Guid.NewGuid() };

        await Should.ThrowAsync<NotFoundException>(() => Handler().HandleAsync(command, CancellationToken.None));
    }
}

public sealed class GetOrderQueryHandlerTests
{
    private readonly IOrderReadModel _orders = Substitute.For<IOrderReadModel>();

    [Fact]
    public async Task Customers_can_only_see_their_own_orders()
    {
        var orderId = Guid.NewGuid();

        await Should.ThrowAsync<NotFoundException>(
            () => new GetOrderQueryHandler(_orders).HandleAsync(orderId, new Caller("user-1", IsAdmin: false), CancellationToken.None));

        await _orders.Received(1).GetAsync(orderId, "user-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Admins_can_see_any_order()
    {
        var orderId = Guid.NewGuid();

        await Should.ThrowAsync<NotFoundException>(
            () => new GetOrderQueryHandler(_orders).HandleAsync(orderId, new Caller("admin", IsAdmin: true), CancellationToken.None));

        await _orders.Received(1).GetAsync(orderId, null, Arg.Any<CancellationToken>());
    }
}
