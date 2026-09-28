using System.Net;
using System.Net.Http.Json;
using Ticketing.Api.Contracts;
using Ticketing.Api.IntegrationTests.Infrastructure;
using Ticketing.Application.Events;
using Ticketing.Application.Reports;

namespace Ticketing.Api.IntegrationTests;

[Collection(SharedApi.Name)]
public sealed class ReportsApiTests(ApiFactory factory)
{
    [Fact]
    public async Task Event_summary_breaks_sales_down_by_tier_using_the_prices_customers_actually_paid()
    {
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.NewEvent(vip: 10, ga: 90, vipPrice: 150m, gaPrice: 50m));
        var vip = created.Tiers.Single(t => t.Name == "VIP");
        var ga = created.Tiers.Single(t => t.Name == "General Admission");
        var buyer = await factory.Buyer();
        await buyer.PurchaseAsync(created.Id, vip.Id, 2);
        await buyer.PurchaseAsync(created.Id, ga.Id, 4);

        // Raise the GA price after those sales: revenue must still reflect the original 50.00.
        var current = (await factory.Anonymous().GetFromJsonAsync<EventDto>($"/api/v1/events/{created.Id}", TestApi.Json))!;
        var repriced = new UpdateEventRequest(
            current.Name, current.Description, current.Venue, current.StartsAt, current.Currency, current.TotalCapacity,
            current.Tiers.Select(t => new PricingTierRequest(t.Id, t.Name, t.Id == ga.Id ? 80m : t.Price, t.Capacity)).ToList());
        (await admin.PutAsJsonAsync($"/api/v1/events/{created.Id}", repriced, TestApi.Json)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await buyer.PurchaseAsync(created.Id, ga.Id, 1);

        var summary = await (await factory.ReportReader()).GetFromJsonAsync<EventSalesSummaryDto>(
            $"/api/v1/events/{created.Id}/sales-summary", TestApi.Json);

        summary!.TicketsSold.ShouldBe(7);
        summary.TicketsRemaining.ShouldBe(93);
        summary.OrderCount.ShouldBe(3);
        summary.SellThroughPercent.ShouldBe(7.0m);
        summary.GrossRevenue.ShouldBe((2 * 150m) + (4 * 50m) + (1 * 80m));
        var gaSales = summary.Tiers.Single(t => t.TierId == ga.Id);
        gaSales.TicketsSold.ShouldBe(5);
        gaSales.GrossRevenue.ShouldBe(280m);
        gaSales.CurrentPrice.ShouldBe(80m);
    }

    [Fact]
    public async Task Portfolio_report_totals_each_currency_separately()
    {
        var admin = await factory.Admin();
        // A private time window keeps this test independent of events created by other tests.
        var window = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(Random.Shared.Next(0, 500_000));
        var usd = await admin.CreateEventAsync(TestApi.SingleTierEvent(capacity: 10, price: 20m, currency: "USD", startsAt: window));
        var eur = await admin.CreateEventAsync(TestApi.SingleTierEvent(capacity: 10, price: 30m, currency: "EUR", startsAt: window));
        await admin.CreateEventAsync(TestApi.SingleTierEvent(capacity: 10, price: 99m, currency: "USD", startsAt: window.AddHours(1)));
        var buyer = await factory.Buyer();
        await buyer.PurchaseAsync(usd.Id, usd.Tiers[0].Id, 3);
        await buyer.PurchaseAsync(eur.Id, eur.Tiers[0].Id, 2);

        var from = Uri.EscapeDataString(window.ToString("O"));
        var to = Uri.EscapeDataString(window.AddMinutes(1).ToString("O"));
        var report = await (await factory.ReportReader()).GetFromJsonAsync<SalesReportDto>(
            $"/api/v1/reports/sales?from={from}&to={to}", TestApi.Json);

        report!.TotalEvents.ShouldBe(2);
        report.Events.Select(e => e.EventId).ShouldBe([usd.Id, eur.Id], ignoreOrder: true);
        report.Totals.ShouldBe(
        [
            new CurrencyTotalDto("EUR", EventCount: 1, TicketsSold: 2, OrderCount: 1, GrossRevenue: 60m),
            new CurrencyTotalDto("USD", EventCount: 1, TicketsSold: 3, OrderCount: 1, GrossRevenue: 60m),
        ]);
    }

    [Fact]
    public async Task Reports_require_the_reports_role()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 5));
        var buyer = await factory.Buyer();

        (await buyer.GetAsync("/api/v1/reports/sales")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await buyer.GetAsync($"/api/v1/events/{created.Id}/sales-summary")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.Anonymous().GetAsync("/api/v1/reports/sales")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Report_rejects_an_inverted_date_range()
    {
        var response = await (await factory.ReportReader()).GetAsync("/api/v1/reports/sales?from=2031-02-01T00:00:00Z&to=2031-01-01T00:00:00Z");

        await response.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
    }
}
