using System.Buffers.Binary;
using System.Globalization;

namespace Ticketing.Infrastructure.Persistence;

/// <summary>Encodes SQL Server rowversion values as short opaque strings for use as ETags.</summary>
internal static class RowVersion
{
    public static string Encode(byte[]? version) =>
        version is { Length: 8 }
            ? BinaryPrimitives.ReadUInt64BigEndian(version).ToString("x16", CultureInfo.InvariantCulture)
            : string.Empty;
}
