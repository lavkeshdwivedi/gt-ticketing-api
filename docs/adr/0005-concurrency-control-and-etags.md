# ADR 0005: Optimistic concurrency and ETags for admin edits

Status: Accepted

## Context

Two admins can edit the same event at the same time. Without a check, the second save silently overwrites the first (lost update). Admin edits are rare and low contention, which is exactly where optimistic concurrency fits.

## Decision

- The `Events` table has a SQL Server `rowversion` column, mapped as an EF Core shadow property so this persistence detail stays out of the domain model.
- `GET /events/{id}` returns it as a strong `ETag`. `If-None-Match` returns `304`.
- `PUT /events/{id}` honours `If-Match`: a stale value returns `412 Precondition Failed` before any change is made. Between the check and the save, EF's concurrency token closes the remaining race; a conflict detected at save time also maps to `412`.
- Without `If-Match` the update still runs, but a concurrent change detected at save time returns `409 concurrency_conflict`.
- Every purchase bumps the same rowversion (ADR 0001), so an edit based on stale inventory also fails.

## Alternatives considered

- **Require If-Match (`428 Precondition Required` when missing).** The most correct HTTP. I would require it in production; it is optional here so the API is easy to explore from Swagger.
- **Last write wins.** Not acceptable for data that controls what customers can buy.

## Consequences

- Clients doing read-modify-write should keep the ETag and send it back; the Angular section of the README shows an interceptor that does this.
