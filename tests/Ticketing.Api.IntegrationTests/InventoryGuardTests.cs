using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Api.IntegrationTests.Infrastructure;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Common;
using Ticketing.Application.Events;
using Ticketing.Domain.Common;
using Ticketing.Domain.Events;
using Ticketing.Domain.Orders;

namespace Ticketing.Api.IntegrationTests;

/// <summary>
/// Exercises the hand-written reservation SQL directly against SQL Server, including the
/// "state changed between read and write" branches that the HTTP path only hits under races.
/// </summary>
[Collection(SharedApi.Name)]
public sealed class InventoryGuardTests(ApiFactory factory)
{
    private async Task<ReservationOutcome> Reserve(
        EventDto @event, int quantity, DateTimeOffset? now = null, decimal? price = null, string? currency = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var inventory = scope.ServiceProvider.GetRequiredService<ITicketInventory>();
        var expected = Money.Of(price ?? @event.Tiers[0].Price, currency ?? @event.Currency);
        return await unitOfWork.ExecuteInTransactionAsync(
            ct => inventory.TryReserveAsync(@event.Id, @event.Tiers[0].Id, quantity, expected, now ?? DateTimeOffset.UtcNow, ct),
            CancellationToken.None);
    }

    [Fact]
    public async Task Reserves_up_to_capacity_and_not_one_seat_more()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 3));

        (await Reserve(created, 2)).ShouldBe(ReservationOutcome.Reserved);
        (await Reserve(created, 2)).ShouldBe(ReservationOutcome.InsufficientInventory);
        (await Reserve(created, 1)).ShouldBe(ReservationOutcome.Reserved);
        (await Reserve(created, 1)).ShouldBe(ReservationOutcome.InsufficientInventory);
    }

    [Fact]
    public async Task A_price_changed_since_the_buyers_snapshot_is_refused_and_no_seat_is_taken()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 3, price: 40m));

        (await Reserve(created, 1, price: 35m)).ShouldBe(ReservationOutcome.PriceChanged);
        (await Reserve(created, 1, currency: "EUR")).ShouldBe(ReservationOutcome.PriceChanged);

        var availability = await factory.Anonymous().GetFromJsonAsync<AvailabilityDto>(
            $"/api/v1/events/{created.Id}/availability", TestApi.Json);
        availability!.Available.ShouldBe(3);
        (await Reserve(created, 1)).ShouldBe(ReservationOutcome.Reserved);
    }

    [Fact]
    public async Task A_changed_price_is_reported_even_when_the_tier_is_also_sold_out()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 1, price: 40m));
        (await Reserve(created, 1)).ShouldBe(ReservationOutcome.Reserved);

        (await Reserve(created, 1, price: 35m)).ShouldBe(ReservationOutcome.PriceChanged);
        (await Reserve(created, 1)).ShouldBe(ReservationOutcome.InsufficientInventory);
    }

    [Fact]
    public async Task Cancelled_event_is_not_on_sale()
    {
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.SingleTierEvent(capacity: 3));
        await admin.PostAsync($"/api/v1/events/{created.Id}/cancel", null);

        (await Reserve(created, 1)).ShouldBe(ReservationOutcome.EventNotOnSale);
    }

    [Fact]
    public async Task Deleted_event_is_not_on_sale()
    {
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.SingleTierEvent(capacity: 3));
        (await admin.DeleteAsync($"/api/v1/events/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Reserve(created, 1)).ShouldBe(ReservationOutcome.EventNotOnSale);
    }

    [Fact]
    public async Task Event_that_has_started_is_not_on_sale()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 3));

        (await Reserve(created, 1, now: created.StartsAt)).ShouldBe(ReservationOutcome.EventNotOnSale);
    }

    [Fact]
    public async Task An_order_for_a_tier_removed_concurrently_is_a_conflict_not_a_server_error()
    {
        // The buyer's snapshot references a tier that no longer exists in the database, as if an
        // admin removed it between the buyer's read and the order insert.
        var snapshot = Event.Create(
            "Ghost", null, "Nowhere", DateTimeOffset.UtcNow.AddDays(30), "USD", 1,
            [new PricingTierDefinition(null, "Removed", 10m, 1)], DateTimeOffset.UtcNow);
        var order = TicketOrder.Place(snapshot, snapshot.Tiers[0], 1, "Ada", "ada@example.com", "user-1", null, null, DateTimeOffset.UtcNow);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITicketOrderRepository>().Add(order);

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Refuses_to_run_outside_a_transaction()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 3));
        await using var scope = factory.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<ITicketInventory>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => inventory.TryReserveAsync(
                created.Id, created.Tiers[0].Id, 1, Money.Of(created.Tiers[0].Price, created.Currency), DateTimeOffset.UtcNow, CancellationToken.None));
    }
}
