namespace Ticketing.Domain.Common;

/// <summary>
/// Generates GUIDs whose most significant bytes (in SQL Server's uniqueidentifier sort order)
/// are time based, so inserts append to the clustered index instead of splitting random pages.
/// Same algorithm EF Core's SequentialGuidValueGenerator uses; kept here so the domain can
/// assign identities without depending on EF.
/// </summary>
public static class SequentialGuid
{
    private static long _counter = DateTime.UtcNow.Ticks;

    public static Guid NewGuid()
    {
        var guidBytes = Guid.NewGuid().ToByteArray();
        var counterBytes = BitConverter.GetBytes(Interlocked.Increment(ref _counter));

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        guidBytes[08] = counterBytes[1];
        guidBytes[09] = counterBytes[0];
        guidBytes[10] = counterBytes[7];
        guidBytes[11] = counterBytes[6];
        guidBytes[12] = counterBytes[5];
        guidBytes[13] = counterBytes[4];
        guidBytes[14] = counterBytes[3];
        guidBytes[15] = counterBytes[2];

        return new Guid(guidBytes);
    }
}
