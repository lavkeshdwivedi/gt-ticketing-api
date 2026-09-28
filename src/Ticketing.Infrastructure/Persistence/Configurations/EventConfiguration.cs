using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Events;

namespace Ticketing.Infrastructure.Persistence.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    /// <summary>
    /// rowversion shadow property: the aggregate's concurrency token and the source of the HTTP ETag.
    /// Kept out of the domain model because it is a persistence concern.
    /// </summary>
    public const string Version = nameof(Version);

    /// <summary>Touched by every purchase so that each sale bumps <see cref="Version"/> (see ADR 0001).</summary>
    public const string LastSoldAt = nameof(LastSoldAt);

    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events", t => t.HasCheckConstraint("CK_Events_TotalCapacity", "[TotalCapacity] > 0"));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Name).HasMaxLength(Event.NameMaxLength).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(Event.DescriptionMaxLength);
        builder.Property(e => e.Venue).HasMaxLength(Event.VenueMaxLength).IsRequired();
        builder.Property(e => e.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);

        builder.Property<byte[]>(Version).IsRowVersion();
        builder.Property<DateTimeOffset?>(LastSoldAt);

        builder.Ignore(e => e.TicketsSold);
        builder.Ignore(e => e.TicketsAvailable);

        builder.HasMany(e => e.Tiers)
            .WithOne()
            .HasForeignKey(t => t.EventId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Tiers).HasField("_tiers").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Soft delete: deleted events disappear from every query, including the purchase path.
        builder.HasQueryFilter(e => !e.IsDeleted);
        builder.HasIndex(e => e.StartsAt).HasFilter("[IsDeleted] = 0");
    }
}
