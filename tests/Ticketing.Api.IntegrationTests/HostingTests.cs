using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Ticketing.Api.Auth;
using Ticketing.Api.IntegrationTests.Infrastructure;

namespace Ticketing.Api.IntegrationTests;

[Collection(SharedApi.Name)]
public sealed class HostingTests(ApiFactory factory)
{
    [Fact]
    public async Task Readiness_probe_checks_the_database()
    {
        var response = await factory.Anonymous().GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Tokens_signed_with_another_key_are_rejected()
    {
        var client = factory.Anonymous();
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJhdHRhY2tlciIsInJvbGVzIjpbImV2ZW50cy5tYW5hZ2UiXSwiYXVkIjoidGlja2V0aW5nLWFwaSIsImlzcyI6InRpY2tldGluZy1kZXYiLCJleHAiOjQxMDI0NDQ4MDB9.c2lnbmF0dXJlLW5vdC12YWxpZA");

        var response = await client.PostAsJsonAsync("/api/v1/events", TestApi.NewEvent(), TestApi.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

/// <summary>Production must never expose development conveniences.</summary>
public sealed class ProductionHostingTests
{
    [Fact]
    public async Task Dev_token_endpoint_does_not_exist_in_production()
    {
        await using var factory = new ProductionFactory(authority: "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0");

        var response = await factory.CreateClient().PostAsJsonAsync("/dev/token", new DevTokenRequest("attacker", [Roles.EventsManage]));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Production_refuses_to_start_without_an_identity_provider()
    {
        await using var factory = new ProductionFactory(authority: null);

        var ex = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        ex.Message.ShouldContain("Auth:Authority");
    }

    private sealed class ProductionFactory(string? authority) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Ticketing", "Server=unused;Database=unused;TrustServerCertificate=true");
            builder.UseSetting("Auth:Authority", authority ?? string.Empty);
        }
    }
}
