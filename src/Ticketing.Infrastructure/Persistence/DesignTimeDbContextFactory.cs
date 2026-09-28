using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ticketing.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` (adding migrations, building the migration bundle, `database update`),
/// never by the running API. The bundle and `database update` normally receive `--connection`;
/// the fallback is the local docker compose database.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TicketingDbContext>
{
    private const string LocalComposeDatabase =
        "Server=localhost,1433;Database=Ticketing;User Id=sa;Password=Local_Dev_Only_P@ssw0rd;TrustServerCertificate=true;Encrypt=true";

    public TicketingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TicketingDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("TICKETING_CONNECTION") ?? LocalComposeDatabase)
            .Options);
}
