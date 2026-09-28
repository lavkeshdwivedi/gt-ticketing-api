using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Events;
using Ticketing.Domain.Orders;

namespace Ticketing.Infrastructure.Persistence.Configurations;

internal sealed class TicketOrderConfiguration : IEntityTypeConfiguration<TicketOrder>
{
    public const string IdempotencyIndex = "UX_TicketOrders_PurchasedBy_IdempotencyKey";

    public void Configure(EntityTypeBuilder<TicketOrder> builder)
    {
        builder.ToTable("TicketOrders", t => t.HasCheckConstraint("CK_TicketOrders_Quantity", "[Quantity] > 0"));

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.TierName).HasMaxLength(PricingTier.NameMaxLength).IsRequired();
        builder.Property(o => o.CustomerName).HasMaxLength(TicketOrder.CustomerNameMaxLength).IsRequired();
        builder.Property(o => o.CustomerEmail).HasMaxLength(TicketOrder.CustomerEmailMaxLength).IsRequired();
        builder.Property(o => o.PurchasedBy).HasMaxLength(TicketOrder.PurchasedByMaxLength).IsRequired();
        builder.Property(o => o.IdempotencyKey).HasMaxLength(TicketOrder.IdempotencyKeyMaxLength).IsUnicode(false);
        builder.Property(o => o.RequestFingerprint).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.ComplexProperty(o => o.UnitPrice, price =>
        {
            price.Property(m => m.Amount).HasColumnName("UnitPrice").HasPrecision(18, 2);
            price.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsFixedLength().IsUnicode(false);
        });
        builder.Ignore(o => o.Total);

        builder.HasOne<Event>().WithMany().HasForeignKey(o => o.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PricingTier>().WithMany().HasForeignKey(o => o.PricingTierId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Tickets).WithOne().HasForeignKey(t => t.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Tickets).HasField("_tickets").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Keys are scoped per user, so one customer's key can never collide with (or reveal) another's order.
        builder.HasIndex(o => new { o.PurchasedBy, o.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL")
            .HasDatabaseName(IdempotencyIndex);
        builder.HasIndex(o => new { o.PurchasedBy, o.PurchasedAt });
        builder.HasIndex(o => new { o.EventId, o.PricingTierId });
    }
}

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Code).HasMaxLength(Ticket.CodeLength).IsFixedLength().IsUnicode(false).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique();
    }
}
