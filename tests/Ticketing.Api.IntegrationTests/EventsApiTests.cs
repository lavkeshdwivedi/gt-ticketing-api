using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Ticketing.Api.Contracts;
using Ticketing.Api.IntegrationTests.Infrastructure;
using Ticketing.Application.Common;
using Ticketing.Application.Events;
using Ticketing.Domain.Events;

namespace Ticketing.Api.IntegrationTests;

[Collection(SharedApi.Name)]
public sealed class EventsApiTests(ApiFactory factory)
{
    [Fact]
    public async Task Create_returns_201_with_location_and_etag_and_the_event_can_be_read_back()
    {
        var admin = await factory.Admin();

        var response = await admin.PostAsJsonAsync("/api/v1/events", TestApi.NewEvent(currency: "usd"), TestApi.Json);

        await response.ShouldHaveStatus(HttpStatusCode.Created);
        response.Headers.ETag.ShouldNotBeNull();
        var created = await response.ReadAsync<EventDto>();
        created.Currency.ShouldBe("USD");
        created.Status.ShouldBe(EventStatus.Scheduled);
        created.Tiers.Count.ShouldBe(2);

        var fetched = await factory.Anonymous().GetAsync(response.Headers.Location);
        await fetched.ShouldHaveStatus(HttpStatusCode.OK);
        (await fetched.ReadAsync<EventDto>()).ShouldBeEquivalentTo(created);
        fetched.Headers.ETag.ShouldBe(response.Headers.ETag);
    }

    [Fact]
    public async Task Creating_events_requires_the_events_manage_role()
    {
        var anonymous = await factory.Anonymous().PostAsJsonAsync("/api/v1/events", TestApi.NewEvent(), TestApi.Json);
        var buyer = await (await factory.Buyer()).PostAsJsonAsync("/api/v1/events", TestApi.NewEvent(), TestApi.Json);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        buyer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invalid_payload_returns_400_with_field_level_errors()
    {
        var admin = await factory.Admin();
        var request = TestApi.NewEvent() with { Name = "", Currency = "dollars", Tiers = null };

        var response = await admin.PostAsJsonAsync("/api/v1/events", request, TestApi.Json);

        var problem = await response.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("name", out _).ShouldBeTrue();
        errors.TryGetProperty("currency", out _).ShouldBeTrue();
        errors.TryGetProperty("tiers", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Tier_allocations_that_do_not_match_capacity_return_422_with_a_stable_code()
    {
        var admin = await factory.Admin();
        var request = TestApi.NewEvent() with { TotalCapacity = 500 };

        var response = await admin.PostAsJsonAsync("/api/v1/events", request, TestApi.Json);

        await response.ShouldBeProblem(HttpStatusCode.UnprocessableEntity, "event.capacity_mismatch");
    }

    [Fact]
    public async Task Start_time_without_an_explicit_offset_is_rejected()
    {
        var admin = await factory.Admin();
        const string json = """
            {"name":"Gig","venue":"Club","startsAt":"2031-10-01T19:00:00","currency":"USD","totalCapacity":10,
             "tiers":[{"name":"GA","price":10,"capacity":10}]}
            """;

        var response = await admin.PostAsync("/api/v1/events", new StringContent(json, Encoding.UTF8, "application/json"));

        await response.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Unknown_event_returns_404_problem()
    {
        var response = await factory.Anonymous().GetAsync($"/api/v1/events/{Guid.NewGuid()}");

        await response.ShouldBeProblem(HttpStatusCode.NotFound, "event.not_found");
    }

    [Fact]
    public async Task Get_with_matching_if_none_match_returns_304()
    {
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.NewEvent());
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/events/{created.Id}");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{created.Version}\""));

        var response = await factory.Anonymous().SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Update_with_current_etag_succeeds_and_a_stale_etag_is_rejected_with_412()
    {
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.NewEvent());

        var first = await admin.SendAsync(Put(created, name: "Renamed once", ifMatch: created.Version));
        await first.ShouldHaveStatus(HttpStatusCode.OK);
        var updated = await first.ReadAsync<EventDto>();
        updated.Name.ShouldBe("Renamed once");
        updated.Version.ShouldNotBe(created.Version);

        var stale = await admin.SendAsync(Put(created, name: "Lost update", ifMatch: created.Version));
        await stale.ShouldBeProblem(HttpStatusCode.PreconditionFailed, "precondition_failed");
    }

    [Fact]
    public async Task A_purchase_changes_the_event_version_so_admin_edits_based_on_stale_inventory_are_rejected()
    {
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.NewEvent());
        var purchase = await (await factory.Buyer()).PurchaseAsync(created.Id, created.Tiers[0].Id, 2);
        await purchase.ShouldHaveStatus(HttpStatusCode.Created);

        var response = await admin.SendAsync(Put(created, name: "Edited from stale view", ifMatch: created.Version));

        await response.ShouldBeProblem(HttpStatusCode.PreconditionFailed, "precondition_failed");
    }

    [Fact]
    public async Task Update_cannot_shrink_a_tier_below_tickets_sold()
    {
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.NewEvent(vip: 10, ga: 90));
        var vip = created.Tiers.Single(t => t.Name == "VIP");
        (await (await factory.Buyer()).PurchaseAsync(created.Id, vip.Id, 5)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var current = await factory.Anonymous().GetFromJsonAsync<EventDto>($"/api/v1/events/{created.Id}", TestApi.Json);
        var tiers = current!.Tiers
            .Select(t => new PricingTierRequest(t.Id, t.Name, t.Price, t.Id == vip.Id ? 4 : t.Capacity))
            .ToList();

        var response = await admin.SendAsync(Put(current, tiers: tiers, totalCapacity: 94));

        await response.ShouldBeProblem(HttpStatusCode.Conflict, "tier.capacity_below_sold");
    }

    [Fact]
    public async Task List_is_paged_and_filterable_by_search_term()
    {
        var admin = await factory.Admin();
        var tag = Guid.NewGuid().ToString("N")[..10];
        for (var i = 0; i < 3; i++)
        {
            await admin.CreateEventAsync(TestApi.NewEvent(name: $"Jazz {tag} night {i}", startsAt: TestApi.NextMonth.AddDays(i)));
        }

        var page1 = await factory.Anonymous().GetFromJsonAsync<PagedResult<EventSummaryDto>>(
            $"/api/v1/events?search={tag}&pageSize=2&page=1", TestApi.Json);
        var page2 = await factory.Anonymous().GetFromJsonAsync<PagedResult<EventSummaryDto>>(
            $"/api/v1/events?search={tag}&pageSize=2&page=2", TestApi.Json);

        page1!.TotalCount.ShouldBe(3);
        page1.Items.Count.ShouldBe(2);
        page2!.Items.Count.ShouldBe(1);
        page1.Items.Concat(page2.Items).Select(e => e.StartsAt).ShouldBeInOrder();
        page1.Items[0].TicketsAvailable.ShouldBe(100);
    }

    [Fact]
    public async Task List_rejects_oversized_pages()
    {
        var response = await factory.Anonymous().GetAsync("/api/v1/events?pageSize=1000");

        await response.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Delete_without_sales_soft_deletes_the_event()
    {
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.NewEvent());

        var response = await admin.DeleteAsync($"/api/v1/events/{created.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.Anonymous().GetAsync($"/api/v1/events/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_with_sales_is_refused_in_favour_of_cancelling()
    {
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.NewEvent());
        await (await factory.Buyer()).PurchaseAsync(created.Id, created.Tiers[0].Id, 1);

        var response = await admin.DeleteAsync($"/api/v1/events/{created.Id}");

        await response.ShouldBeProblem(HttpStatusCode.Conflict, "event.has_sales");
    }

    [Fact]
    public async Task Cancelled_event_stops_selling_and_cancel_is_idempotent()
    {
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.NewEvent());

        (await admin.PostAsync($"/api/v1/events/{created.Id}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsync($"/api/v1/events/{created.Id}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var purchase = await (await factory.Buyer()).PurchaseAsync(created.Id, created.Tiers[0].Id, 1);
        await purchase.ShouldBeProblem(HttpStatusCode.Conflict, "event.cancelled");
        var availability = await factory.Anonymous().GetFromJsonAsync<AvailabilityDto>(
            $"/api/v1/events/{created.Id}/availability", TestApi.Json);
        availability!.IsOnSale.ShouldBeFalse();
    }

    private static HttpRequestMessage Put(
        EventDto current,
        string? name = null,
        string? ifMatch = null,
        IReadOnlyList<PricingTierRequest>? tiers = null,
        int? totalCapacity = null)
    {
        var body = new UpdateEventRequest(
            name ?? current.Name,
            current.Description,
            current.Venue,
            current.StartsAt,
            current.Currency,
            totalCapacity ?? current.TotalCapacity,
            tiers ?? current.Tiers.Select(t => new PricingTierRequest(t.Id, t.Name, t.Price, t.Capacity)).ToList());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/events/{current.Id}")
        {
            Content = JsonContent.Create(body, options: TestApi.Json),
        };
        if (ifMatch is not null)
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{ifMatch}\""));
        }

        return request;
    }
}
