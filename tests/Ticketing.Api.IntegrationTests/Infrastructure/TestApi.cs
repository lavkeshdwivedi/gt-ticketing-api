using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ticketing.Api.Contracts;
using Ticketing.Application.Events;

namespace Ticketing.Api.IntegrationTests.Infrastructure;

/// <summary>Small helpers so tests read as scenarios rather than HTTP plumbing.</summary>
internal static class TestApi
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static DateTimeOffset NextMonth => DateTimeOffset.UtcNow.AddDays(30);

    public static CreateEventRequest NewEvent(
        int vip = 10,
        int ga = 90,
        decimal vipPrice = 150m,
        decimal gaPrice = 50m,
        string currency = "USD",
        DateTimeOffset? startsAt = null,
        string? name = null) => new(
        name ?? $"Concert {Guid.NewGuid():N}",
        "Integration test event",
        "Main Hall",
        startsAt ?? NextMonth,
        currency,
        vip + ga,
        [new PricingTierRequest(null, "VIP", vipPrice, vip), new PricingTierRequest(null, "General Admission", gaPrice, ga)]);

    public static CreateEventRequest SingleTierEvent(int capacity, decimal price = 25m, string currency = "USD", DateTimeOffset? startsAt = null) => new(
        $"Show {Guid.NewGuid():N}",
        null,
        "Studio",
        startsAt ?? NextMonth,
        currency,
        capacity,
        [new PricingTierRequest(null, "General Admission", price, capacity)]);

    public static async Task<EventDto> CreateEventAsync(this HttpClient admin, CreateEventRequest request)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/events", request, Json);
        await response.ShouldHaveStatus(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<EventDto>(Json))!;
    }

    public static Task<HttpResponseMessage> PurchaseAsync(
        this HttpClient buyer, Guid eventId, Guid tierId, int quantity, string? idempotencyKey = null, string email = "buyer@example.com")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/events/{eventId}/purchases")
        {
            Content = JsonContent.Create(new PurchaseTicketsRequest(tierId, quantity, "Test Buyer", email), options: Json),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return buyer.SendAsync(request);
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task ShouldHaveStatus(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(expected, $"Body: {body}");
        }
    }

    /// <summary>Asserts an RFC 7807 problem response with the given status and stable error code.</summary>
    public static async Task<JsonElement> ShouldBeProblem(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await response.ShouldHaveStatus(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.TryGetProperty("traceId", out _).ShouldBeTrue();
        return problem;
    }
}
