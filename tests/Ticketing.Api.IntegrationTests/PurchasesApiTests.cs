using System.Net;
using System.Net.Http.Json;
using Ticketing.Api.IntegrationTests.Infrastructure;
using Ticketing.Application.Events;
using Ticketing.Application.Orders;

namespace Ticketing.Api.IntegrationTests;

[Collection(SharedApi.Name)]
public sealed class PurchasesApiTests(ApiFactory factory)
{
    [Fact]
    public async Task Purchasing_requires_an_authenticated_user()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.NewEvent());

        var response = await factory.Anonymous().PurchaseAsync(created.Id, created.Tiers[0].Id, 1);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Purchase_issues_tickets_reduces_availability_and_is_readable_only_by_its_owner()
    {
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.NewEvent(vip: 10, ga: 90, vipPrice: 150m));
        var vip = created.Tiers.Single(t => t.Name == "VIP");
        var buyer = await factory.Buyer();

        var response = await buyer.PurchaseAsync(created.Id, vip.Id, 3);

        await response.ShouldHaveStatus(HttpStatusCode.Created);
        var order = await response.ReadAsync<OrderDto>();
        order.Quantity.ShouldBe(3);
        order.UnitPrice.ShouldBe(150m);
        order.Total.ShouldBe(450m);
        order.Currency.ShouldBe("USD");
        order.Tickets.Count.ShouldBe(3);
        response.Headers.Location!.ToString().ShouldEndWith($"/api/v1/orders/{order.Id}");

        var availability = await factory.Anonymous().GetFromJsonAsync<AvailabilityDto>(
            $"/api/v1/events/{created.Id}/availability", TestApi.Json);
        availability!.Available.ShouldBe(97);
        availability.Tiers.Single(t => t.TierId == vip.Id).Available.ShouldBe(7);

        (await buyer.GetAsync(response.Headers.Location)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync(response.Headers.Location)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var stranger = await (await factory.Buyer()).GetAsync(response.Headers.Location);
        await stranger.ShouldBeProblem(HttpStatusCode.NotFound, "order.not_found");

        var mine = await buyer.GetFromJsonAsync<List<OrderDto>>("/api/v1/orders", TestApi.Json);
        mine!.ShouldHaveSingleItem().Id.ShouldBe(order.Id);
    }

    [Fact]
    public async Task Buying_more_than_remains_returns_409_and_sells_nothing()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 5));
        var tier = created.Tiers[0].Id;
        var buyer = await factory.Buyer();
        (await buyer.PurchaseAsync(created.Id, tier, 4)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await buyer.PurchaseAsync(created.Id, tier, 2);

        var problem = await response.ShouldBeProblem(HttpStatusCode.Conflict, "tickets.sold_out");
        problem.GetProperty("detail").GetString().ShouldBe("Only 1 tickets remain in tier 'General Admission'.");
        var availability = await factory.Anonymous().GetFromJsonAsync<AvailabilityDto>(
            $"/api/v1/events/{created.Id}/availability", TestApi.Json);
        availability!.Available.ShouldBe(1);
    }

    [Fact]
    public async Task Same_idempotency_key_replays_the_original_order()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 10));
        var buyer = await factory.Buyer();
        var key = Guid.NewGuid().ToString();

        var first = await buyer.PurchaseAsync(created.Id, created.Tiers[0].Id, 2, key);
        var retry = await buyer.PurchaseAsync(created.Id, created.Tiers[0].Id, 2, key);

        await first.ShouldHaveStatus(HttpStatusCode.Created);
        await retry.ShouldHaveStatus(HttpStatusCode.Created);
        first.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        retry.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await retry.ReadAsync<OrderDto>()).Id.ShouldBe((await first.ReadAsync<OrderDto>()).Id);

        var availability = await factory.Anonymous().GetFromJsonAsync<AvailabilityDto>(
            $"/api/v1/events/{created.Id}/availability", TestApi.Json);
        availability!.Available.ShouldBe(8);
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_with_a_different_request_is_rejected()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 10));
        var buyer = await factory.Buyer();
        var key = Guid.NewGuid().ToString();
        await buyer.PurchaseAsync(created.Id, created.Tiers[0].Id, 2, key);

        var response = await buyer.PurchaseAsync(created.Id, created.Tiers[0].Id, 5, key);

        await response.ShouldBeProblem(HttpStatusCode.UnprocessableEntity, "idempotency_key_reused");
    }

    [Fact]
    public async Task Idempotency_keys_are_scoped_per_user()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 10));
        const string sharedKey = "order-1";

        var alice = await (await factory.Buyer()).PurchaseAsync(created.Id, created.Tiers[0].Id, 1, sharedKey);
        var bob = await (await factory.Buyer()).PurchaseAsync(created.Id, created.Tiers[0].Id, 1, sharedKey);

        await alice.ShouldHaveStatus(HttpStatusCode.Created);
        await bob.ShouldHaveStatus(HttpStatusCode.Created);
        (await bob.ReadAsync<OrderDto>()).Id.ShouldNotBe((await alice.ReadAsync<OrderDto>()).Id);
    }

    [Fact]
    public async Task Concurrent_retries_with_the_same_key_create_exactly_one_order()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 50));
        var buyer = await factory.Buyer();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => buyer.PurchaseAsync(created.Id, created.Tiers[0].Id, 2, key)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        var orderIds = await Task.WhenAll(responses.Select(async r => (await r.ReadAsync<OrderDto>()).Id));
        orderIds.Distinct().ShouldHaveSingleItem();
        var availability = await factory.Anonymous().GetFromJsonAsync<AvailabilityDto>(
            $"/api/v1/events/{created.Id}/availability", TestApi.Json);
        availability!.Available.ShouldBe(48);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Quantity_outside_1_to_10_is_a_validation_error(int quantity)
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 50));

        var response = await (await factory.Buyer()).PurchaseAsync(created.Id, created.Tiers[0].Id, quantity);

        await response.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Tier_from_another_event_is_rejected()
    {
        var admin = await factory.Admin();
        var first = await admin.CreateEventAsync(TestApi.SingleTierEvent(capacity: 5));
        var second = await admin.CreateEventAsync(TestApi.SingleTierEvent(capacity: 5));

        var response = await (await factory.Buyer()).PurchaseAsync(first.Id, second.Tiers[0].Id, 1);

        await response.ShouldBeProblem(HttpStatusCode.UnprocessableEntity, "tier.unknown");
    }

    [Fact]
    public async Task Unknown_event_returns_404()
    {
        var response = await (await factory.Buyer()).PurchaseAsync(Guid.NewGuid(), Guid.NewGuid(), 1);

        await response.ShouldBeProblem(HttpStatusCode.NotFound, "event.not_found");
    }
}
