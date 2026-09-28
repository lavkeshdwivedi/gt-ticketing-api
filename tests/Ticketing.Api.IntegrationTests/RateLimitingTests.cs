using System.Net;
using Microsoft.AspNetCore.Hosting;
using Ticketing.Api.IntegrationTests.Infrastructure;

namespace Ticketing.Api.IntegrationTests;

[Collection(SharedApi.Name)]
public sealed class RateLimitingTests(ApiFactory factory)
{
    [Fact]
    public async Task A_single_user_hammering_purchases_is_throttled_with_429_and_retry_after()
    {
        await using var throttled = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:Purchases:TokenLimit", "3");
            b.UseSetting("RateLimiting:Purchases:TokensPerPeriod", "3");
            b.UseSetting("RateLimiting:Purchases:PeriodSeconds", "60");
        });
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.SingleTierEvent(capacity: 100));
        var botClient = throttled.CreateClient();
        botClient.DefaultRequestHeaders.Authorization = (await factory.Buyer()).DefaultRequestHeaders.Authorization;
        var otherUser = throttled.CreateClient();
        otherUser.DefaultRequestHeaders.Authorization = (await factory.Buyer()).DefaultRequestHeaders.Authorization;

        var statuses = new List<HttpStatusCode>();
        var last = new HttpResponseMessage();
        for (var i = 0; i < 5; i++)
        {
            last = await botClient.PurchaseAsync(created.Id, created.Tiers[0].Id, 1);
            statuses.Add(last.StatusCode);
        }

        statuses.ShouldBe([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.Created,
            HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests]);
        await last.ShouldBeProblem(HttpStatusCode.TooManyRequests, "rate_limited");
        last.Headers.RetryAfter.ShouldNotBeNull();

        // Limits are per user: someone else is unaffected by the bot.
        (await otherUser.PurchaseAsync(created.Id, created.Tiers[0].Id, 1)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
