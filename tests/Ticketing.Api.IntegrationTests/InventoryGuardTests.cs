using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Api.IntegrationTests.Infrastructure;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Events;

namespace Ticketing.Api.IntegrationTests;

/// <summary>
/// Exercises the hand-written reservation SQL directly against SQL Server, including the
/// "state changed between read and write" branches that the HTTP path only hits under races.
/// </summary>
[Collection(SharedApi.Name)]
public sealed class InventoryGuardTests(ApiFactory factory)
{
    private async Task<ReservationOutcome> Reserve(EventDto @event, int quantity, DateTimeOffset? now = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var inventory = scope.ServiceProvider.GetRequiredService<ITicketInventory>();
        return await unitOfWork.ExecuteInTransactionAsync(
            ct => inventory.TryReserveAsync(@event.Id, @event.Tiers[0].Id, quantity, now ?? DateTimeOffset.UtcNow, ct),
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
    public async Task Refuses_to_run_outside_a_transaction()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 3));
        await using var scope = factory.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<ITicketInventory>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => inventory.TryReserveAsync(created.Id, created.Tiers[0].Id, 1, DateTimeOffset.UtcNow, CancellationToken.None));
    }
}
