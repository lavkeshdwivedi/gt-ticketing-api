using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Ticketing.Api.IntegrationTests.Infrastructure;
using Ticketing.Application.Events;
using Ticketing.Application.Orders;
using Ticketing.Application.Reports;

namespace Ticketing.Api.IntegrationTests;

/// <summary>
/// The core guarantee of the system, tested the only way that means anything: many real concurrent
/// HTTP requests against a real SQL Server, then reconciling every view of the numbers.
/// </summary>
[Collection(SharedApi.Name)]
public sealed class OversellingTests(ApiFactory factory)
{
    [Fact]
    public async Task Two_hundred_buyers_racing_for_fifty_seats_sell_exactly_fifty()
    {
        const int seats = 50;
        const int buyers = 200;
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.SingleTierEvent(capacity: seats, price: 40m));
        var clients = await Task.WhenAll(Enumerable.Range(0, buyers).Select(_ => factory.Buyer()));

        // Every request waits on the same gate so they hit the server together, not staggered.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var purchases = clients.Select(async client =>
        {
            await gate.Task;
            return await client.PurchaseAsync(created.Id, created.Tiers[0].Id, 1);
        }).ToList();
        gate.SetResult();
        var responses = await Task.WhenAll(purchases);

        var byStatus = responses.GroupBy(r => r.StatusCode).ToDictionary(g => g.Key, g => g.Count());
        byStatus.GetValueOrDefault(HttpStatusCode.Created).ShouldBe(seats);
        byStatus.GetValueOrDefault(HttpStatusCode.Conflict).ShouldBe(buyers - seats);
        byStatus.Keys.ShouldBeSubsetOf([HttpStatusCode.Created, HttpStatusCode.Conflict]);

        await AssertBooksReconcile(created, expectedSold: seats, expectedRevenue: seats * 40m);
    }

    [Fact]
    public async Task Mixed_quantity_orders_under_contention_never_exceed_capacity()
    {
        const int seats = 30;
        var admin = await factory.Admin();
        var created = await admin.CreateEventAsync(TestApi.SingleTierEvent(capacity: seats, price: 10m));
        var clients = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => factory.Buyer()));

        var responses = await Task.WhenAll(clients.Select((client, i) =>
            client.PurchaseAsync(created.Id, created.Tiers[0].Id, quantity: (i % 4) + 1)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);
        var orders = await Task.WhenAll(responses
            .Where(r => r.StatusCode == HttpStatusCode.Created)
            .Select(r => r.ReadAsync<OrderDto>()));
        var sold = orders.Sum(o => o.Quantity);
        sold.ShouldBeLessThanOrEqualTo(seats);
        // With 150 tickets requested for 30 seats, the only way to end below capacity is if every
        // remaining request was larger than what was left.
        (seats - sold).ShouldBeLessThan(4);

        await AssertBooksReconcile(created, expectedSold: sold, expectedRevenue: sold * 10m);
    }

    [Fact]
    public async Task Database_constraint_rejects_oversold_inventory_even_if_application_code_is_bypassed()
    {
        var created = await (await factory.Admin()).CreateEventAsync(TestApi.SingleTierEvent(capacity: 5));
        await using var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("UPDATE PricingTiers SET Sold = 6 WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@id", created.Tiers[0].Id);

        var ex = await Should.ThrowAsync<SqlException>(() => command.ExecuteNonQueryAsync());

        ex.Message.ShouldContain("CK_PricingTiers_Sold");
    }

    /// <summary>Inventory counters, order records and the sales report must all tell the same story.</summary>
    private async Task AssertBooksReconcile(EventDto created, int expectedSold, decimal expectedRevenue)
    {
        var availability = await factory.Anonymous().GetFromJsonAsync<AvailabilityDto>(
            $"/api/v1/events/{created.Id}/availability", TestApi.Json);
        availability!.Available.ShouldBe(created.TotalCapacity - expectedSold);

        var summary = await (await factory.ReportReader()).GetFromJsonAsync<EventSalesSummaryDto>(
            $"/api/v1/events/{created.Id}/sales-summary", TestApi.Json);
        summary!.TicketsSold.ShouldBe(expectedSold);
        summary.TicketsRemaining.ShouldBe(created.TotalCapacity - expectedSold);
        summary.GrossRevenue.ShouldBe(expectedRevenue);

        await using var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT t.Sold,
                   (SELECT COALESCE(SUM(o.Quantity), 0) FROM TicketOrders o WHERE o.PricingTierId = t.Id),
                   (SELECT COUNT(*) FROM Tickets k JOIN TicketOrders o ON o.Id = k.OrderId WHERE o.PricingTierId = t.Id)
            FROM PricingTiers t WHERE t.EventId = @eventId
            """,
            connection);
        command.Parameters.AddWithValue("@eventId", created.Id);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt32(0).ShouldBe(expectedSold, "tier counter");
        reader.GetInt32(1).ShouldBe(expectedSold, "sum of order quantities");
        reader.GetInt32(2).ShouldBe(expectedSold, "tickets issued");
    }
}
