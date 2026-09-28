using System.Globalization;

namespace Ticketing.Infrastructure.Persistence;

/// <summary>Formats an event's admin revision as the opaque version string used for ETags.</summary>
internal static class EventRevision
{
    public static string Encode(int revision) => revision.ToString(CultureInfo.InvariantCulture);
}
