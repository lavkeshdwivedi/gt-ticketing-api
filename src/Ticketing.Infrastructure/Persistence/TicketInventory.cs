using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Events;

namespace Ticketing.Infrastructure.Persistence;

/// <summary>
/// The one hand-written SQL statement in the service: seat reservation on the hot path (ADR 0001).
///
/// Both guarded UPDATEs travel in a single batch, so the event row lock that serialises purchases
/// for one event is held for one round trip plus the commit, rather than across several EF calls.
/// Load testing showed that lock window is what bounds throughput on a popular event.
/// </summary>
internal sealed class TicketInventory(TicketingDbContext db) : ITicketInventory
{
    // Outcome codes returned by the batch.
    private const int Reserved = 0;
    private const int NotOnSale = 1;

    private const string ReserveSql = """
        -- Event must still be on sale. Takes the event row lock and bumps its rowversion, so admin
        -- edits based on a stale view fail their concurrency check instead of racing this sale.
        UPDATE Events SET LastSoldAt = @now
         WHERE Id = @eventId AND Status = @scheduled AND IsDeleted = 0 AND StartsAt > @now;
        IF @@ROWCOUNT = 0
        BEGIN
            SELECT 1;
            RETURN;
        END

        -- Conditional increment: succeeds only if the seats are still there at write time.
        UPDATE PricingTiers SET Sold = Sold + @quantity
         WHERE Id = @tierId AND EventId = @eventId AND Sold + @quantity <= Capacity;
        SELECT CASE WHEN @@ROWCOUNT = 1 THEN 0 ELSE 2 END;
        """;

    public async Task<ReservationOutcome> TryReserveAsync(
        Guid eventId, Guid tierId, int quantity, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Seat reservation must run inside the purchase transaction.");

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await using var command = new SqlCommand(ReserveSql, connection, (SqlTransaction)transaction.GetDbTransaction());
        command.Parameters.Add(new SqlParameter("@eventId", SqlDbType.UniqueIdentifier) { Value = eventId });
        command.Parameters.Add(new SqlParameter("@tierId", SqlDbType.UniqueIdentifier) { Value = tierId });
        command.Parameters.Add(new SqlParameter("@quantity", SqlDbType.Int) { Value = quantity });
        command.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTimeOffset) { Value = now });
        command.Parameters.Add(new SqlParameter("@scheduled", SqlDbType.VarChar, 20) { Value = nameof(EventStatus.Scheduled) });

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return result switch
        {
            Reserved => ReservationOutcome.Reserved,
            NotOnSale => ReservationOutcome.EventNotOnSale,
            _ => ReservationOutcome.InsufficientInventory,
        };
    }
}
