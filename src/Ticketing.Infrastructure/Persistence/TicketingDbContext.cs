using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Events;
using Ticketing.Domain.Orders;
using Ticketing.Infrastructure.Persistence.Configurations;

namespace Ticketing.Infrastructure.Persistence;

public sealed class TicketingDbContext(DbContextOptions<TicketingDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();

    public DbSet<PricingTier> PricingTiers => Set<PricingTier>();

    public DbSet<TicketOrder> TicketOrders => Set<TicketOrder>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampRevisions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampRevisions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TicketingDbContext).Assembly);

    /// <summary>
    /// Every admin change goes through EF and marks the event Modified (each one sets UpdatedAt),
    /// so this is the single place the ETag source moves. Sales use raw SQL and never touch it.
    /// </summary>
    private void StampRevisions()
    {
        foreach (var entry in ChangeTracker.Entries<Event>())
        {
            var revision = entry.Property<int>(EventConfiguration.Revision);
            switch (entry.State)
            {
                case EntityState.Added:
                    revision.CurrentValue = 1;
                    break;
                case EntityState.Modified:
                    revision.CurrentValue = revision.OriginalValue + 1;
                    break;
            }
        }
    }
}
