using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Events;

namespace Ticketing.Infrastructure.Persistence.Configurations;

internal sealed class PricingTierConfiguration : IEntityTypeConfiguration<PricingTier>
{
    public void Configure(EntityTypeBuilder<PricingTier> builder)
    {
        builder.ToTable("PricingTiers", t =>
        {
            t.HasCheckConstraint("CK_PricingTiers_Capacity", "[Capacity] > 0");
            // Defence in depth: even a buggy code path cannot persist an oversold tier.
            t.HasCheckConstraint("CK_PricingTiers_Sold", "[Sold] >= 0 AND [Sold] <= [Capacity]");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(PricingTier.NameMaxLength).IsRequired();
        builder.ComplexProperty(t => t.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("Price").HasPrecision(18, 2);
            price.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsFixedLength().IsUnicode(false);
        });

        builder.Ignore(t => t.Available);
    }
}
