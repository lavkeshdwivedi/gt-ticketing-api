# ADR 0005: Optimistic concurrency and ETags for admin edits

Status: Accepted

## Context

Two admins can edit the same event at the same time. Without a check, the second save silently overwrites the first (lost update). Admin edits are rare and low contention, which is exactly where optimistic concurrency fits.

## Decision

- The `Events` table has an integer `Revision` column, mapped as an EF Core shadow property so this persistence detail stays out of the domain model. It is bumped on every admin change (in `TicketingDbContext`) and never by a sale, because sales go through the raw SQL guard.
- `GET /events/{id}` returns it as a strong `ETag`. `If-None-Match` returns `304`. The event's JSON does not include sales counts, so this ETag really does describe the representation.
- `PUT /events/{id}` honours `If-Match`. The check runs after the admin write has taken the event row lock (ADR 0001), so nothing can change between the check and the save; a stale value returns `412 Precondition Failed`.
- The table also keeps a `rowversion` as EF's concurrency token, a backstop for any write that bypasses the lock. It is not exposed.
- Without `If-Match` the update still runs. It is serialised behind other admin changes, so the last one wins.

The first version used the `rowversion` as the ETag. Since every sale bumped it, an admin editing a popular event during an on-sale got a `412` on almost every attempt. A test now fires 40 purchases and an admin edit together and asserts the edit succeeds.

## Alternatives considered

- **Require If-Match (`428 Precondition Required` when missing).** The most correct HTTP. I would require it in production; it is optional here so the API is easy to explore from Swagger.
- **Last write wins.** Not acceptable for data that controls what customers can buy.

## Consequences

- Clients doing read-modify-write should keep the ETag and send it back; the Angular section of the README shows an interceptor that does this.
