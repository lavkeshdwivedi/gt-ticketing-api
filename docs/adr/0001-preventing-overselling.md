# ADR 0001: Preventing overselling with guarded atomic updates

Status: Accepted

## Context

Ticket inventory is a hot counter. During an on-sale, hundreds of buyers can hit the same tier within the same second, and the system must never sell seat 51 of 50. A classic read-check-write ("load tier, check `Sold + qty <= Capacity`, save") is wrong under concurrency: two requests can both read 49 and both write 50.

A second, subtler race exists between purchases and admin actions. An admin loads an event that shows zero sales and deletes it; a purchase commits in between. The admin's concurrency check only covered the Event row, the purchase only touched the Tier row, so the event would be deleted with a sale on it. The same shape applies to cancelling and to shrinking a tier's capacity.

## Decision

A purchase runs in one database transaction with two guarded, set-based statements, then inserts the order:

```sql
-- 1. Event must still be on sale. Takes the event row lock and bumps its rowversion.
UPDATE Events SET LastSoldAt = @now
 WHERE Id = @eventId AND Status = 'Scheduled' AND IsDeleted = 0 AND StartsAt > @now;
-- 0 rows => 409 event.not_on_sale

-- 2. Conditional increment. Only succeeds if the seats are still there at write time.
UPDATE PricingTiers SET Sold = Sold + @qty
 WHERE Id = @tierId AND EventId = @eventId AND Sold + @qty <= Capacity;
-- 0 rows => 409 tickets.sold_out

INSERT TicketOrders ..., Tickets ...
```

The Event row's `rowversion` is therefore a true aggregate version: every change to the aggregate, including inventory, bumps it. Admin edits, cancels and deletes use optimistic concurrency on that token, so an admin acting on a stale view gets 409 or 412 instead of racing a sale.

Before the transaction, the handler runs every business rule in memory against a snapshot of the aggregate (`Event.ReserveTickets`). That gives precise error messages and avoids a round trip for obviously invalid requests. The snapshot can be stale; the SQL guard is what actually enforces the invariant.

A `CHECK (Sold >= 0 AND Sold <= Capacity)` constraint backs this up, so even a future bug in application code cannot persist an oversold tier.

## Alternatives considered

- **Optimistic concurrency on the tier plus retry.** Keeps the rule purely in the domain model, but under a burst every conflict forces a reload and retry. Retry storms waste database capacity, and a bounded retry means some buyers get "sold out" while seats remain.
- **Pessimistic locking (`SELECT ... WITH (UPDLOCK)`)** then domain logic. Correct, but holds the lock across a round trip to the app server, and Azure SQL's default RCSI isolation makes some cross-table checks subtle.
- **Admin actions lock the tier rows instead of purchases touching the event row.** Keeps purchases on different tiers of the same event fully parallel, at the cost of more code and more subtle isolation reasoning.

## Consequences

- Purchases for the same event serialize on one row lock for the duration of a short transaction. Purchases for different events are fully parallel. The load test in `loadtest/` measures what this costs (see README).
- The rule "enough seats" exists twice: in `PricingTier.Reserve` (in memory, for early and precise errors) and in the SQL guard (authoritative). Both are covered by tests.
- The event's ETag changes after every sale, even though the event's JSON representation does not include sales counts. That is intentional: edits must be based on the current inventory.
- **At flash-sale scale** (a stadium on-sale), I would move to a reservation model: a request queue per event (Azure Service Bus sessions keyed by event id) feeding a single consumer that allocates seats, with short-lived holds that expire unless payment confirms. That trades immediate answers for smooth throughput and fairness.
