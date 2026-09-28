using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Events;
using Ticketing.Domain.Orders;

namespace Ticketing.Infrastructure.Persistence;

public sealed class TicketingDbContext(DbContextOptions<TicketingDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();

    public DbSet<PricingTier> PricingTiers => Set<PricingTier>();

    public DbSet<TicketOrder> TicketOrders => Set<TicketOrder>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TicketingDbContext).Assembly);
}
