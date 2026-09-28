# ADR 0006: Soft delete, and cancellation for events with sales

Status: Accepted

## Context

The brief asks for "delete events". Once tickets are sold, an event is tied to orders, revenue and customers. Physically deleting it destroys financial records; soft deleting it hides purchases that customers still hold.

## Decision

- `DELETE /events/{id}` is allowed only when nothing has been sold. It is a soft delete (`IsDeleted`, `DeletedAt`) with a global query filter, so deleted events disappear from every read and from the purchase path, but the row survives for audit.
- If tickets have been sold, `DELETE` returns `409 event.has_sales` and tells the client to cancel instead.
- `POST /events/{id}/cancel` stops sales, keeps all records and is idempotent (cancelling twice is a no-op, so clients can retry safely).
- Orders reference events and tiers with `ON DELETE RESTRICT`: the database itself refuses to orphan a purchase.

## Consequences

- Refunds on cancellation are out of scope; in a real system cancellation would publish an `EventCancelled` integration event that a payments service consumes to issue refunds.
