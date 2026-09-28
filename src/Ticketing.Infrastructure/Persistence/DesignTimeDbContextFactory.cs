using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ticketing.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` when generating migrations; never at runtime.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TicketingDbContext>
{
    public TicketingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TicketingDbContext>()
            .UseSqlServer("Server=localhost;Database=Ticketing;Integrated Security=true;TrustServerCertificate=true")
            .Options);
}
