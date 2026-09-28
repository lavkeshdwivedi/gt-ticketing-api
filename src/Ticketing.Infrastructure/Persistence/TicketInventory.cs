using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Common;
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
    private const int PriceChanged = 3;

    private const string ReserveSql = """
        -- Event must still be on sale. Takes the event row lock, which admin writes also take
        -- (EventRepository.GetForUpdateAsync), so a sale and an admin change never interleave.
        UPDATE Events SET LastSoldAt = @now
         WHERE Id = @eventId AND Status = @scheduled AND IsDeleted = 0 AND StartsAt > @now;
        IF @@ROWCOUNT = 0
        BEGIN
            SELECT 1;
            RETURN;
        END

        -- Conditional increment: succeeds only if the seats are still there at write time and the
        -- price is still the one the order was placed at.
        UPDATE PricingTiers SET Sold = Sold + @quantity
         WHERE Id = @tierId AND EventId = @eventId AND Sold + @quantity <= Capacity
           AND Price = @price AND Currency = @currency;
        IF @@ROWCOUNT = 1
        BEGIN
            SELECT 0;
            RETURN;
        END

        -- Explain the refusal: a changed price wins over sold out, since the buyer must re-read either way.
        SELECT CASE WHEN EXISTS (
            SELECT 1 FROM PricingTiers
             WHERE Id = @tierId AND EventId = @eventId AND (Price <> @price OR Currency <> @currency))
            THEN 3 ELSE 2 END;
        """;

    public async Task<ReservationOutcome> TryReserveAsync(
        Guid eventId, Guid tierId, int quantity, Money expectedUnitPrice, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedUnitPrice);
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Seat reservation must run inside the purchase transaction.");

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await using var command = new SqlCommand(ReserveSql, connection, (SqlTransaction)transaction.GetDbTransaction());
        command.Parameters.Add(new SqlParameter("@eventId", SqlDbType.UniqueIdentifier) { Value = eventId });
        command.Parameters.Add(new SqlParameter("@tierId", SqlDbType.UniqueIdentifier) { Value = tierId });
        command.Parameters.Add(new SqlParameter("@quantity", SqlDbType.Int) { Value = quantity });
        command.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTimeOffset) { Value = now });
        command.Parameters.Add(new SqlParameter("@price", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = expectedUnitPrice.Amount });
        command.Parameters.Add(new SqlParameter("@currency", SqlDbType.Char, 3) { Value = expectedUnitPrice.Currency });
        command.Parameters.Add(new SqlParameter("@scheduled", SqlDbType.VarChar, 20) { Value = nameof(EventStatus.Scheduled) });

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return result switch
        {
            Reserved => ReservationOutcome.Reserved,
            NotOnSale => ReservationOutcome.EventNotOnSale,
            PriceChanged => ReservationOutcome.PriceChanged,
            _ => ReservationOutcome.InsufficientInventory,
        };
    }
}
