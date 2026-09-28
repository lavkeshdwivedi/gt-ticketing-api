using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Ticketing.Api.Auth;

namespace Ticketing.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API (all middleware, auth, EF Core, migrations) against a throwaway SQL Server
/// container. Shared by every test in the collection; tests isolate themselves by creating their
/// own events instead of resetting the database.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string SigningKey = "integration-test-signing-key-0123456789abcdef";

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        ConnectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "Ticketing" }.ConnectionString;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _sql.DisposeAsync();
        await DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Ticketing", ConnectionString);
        builder.UseSetting("Auth:DevSigningKey", SigningKey);
        builder.UseSetting("Auth:EnableDevTokenEndpoint", "true");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        // Tests exercise inventory under contention, not throttling; RateLimitingTests covers the limiter.
        builder.UseSetting("RateLimiting:Purchases:TokenLimit", "1000");
        builder.UseSetting("RateLimiting:Purchases:TokensPerPeriod", "1000");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
    }

    public HttpClient Anonymous() => CreateClient();

    public Task<HttpClient> Admin() => ClientFor("admin-" + Guid.NewGuid().ToString("N")[..8], Roles.EventsManage, Roles.ReportsRead);

    public Task<HttpClient> Buyer() => ClientFor("buyer-" + Guid.NewGuid().ToString("N")[..8]);

    public Task<HttpClient> ReportReader() => ClientFor("analyst-" + Guid.NewGuid().ToString("N")[..8], Roles.ReportsRead);

    public async Task<HttpClient> ClientFor(string subject, params string[] roles)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/dev/token", new DevTokenRequest(subject, roles));
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<DevTokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }
}

[CollectionDefinition(Name)]
public sealed class SharedApi : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
