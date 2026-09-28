# ADR 0007: Error model: RFC 7807 problems with stable codes

Status: Accepted

## Decision

Every error is `application/problem+json` produced in one place (`GlobalExceptionHandler`), with a machine-readable `code` and the `traceId` for log correlation. Status codes separate three different kinds of "no":

| Status | Meaning | Example codes |
|---|---|---|
| 400 | The request is malformed or fails field validation | `validation_failed` (with per-field `errors`) |
| 401 / 403 | Not signed in / not allowed | |
| 404 | Not found, or not yours to see | `event.not_found`, `order.not_found` |
| 409 | Valid request that conflicts with current state | `tickets.sold_out`, `tier.price_changed`, `event.cancelled`, `event.has_sales`, `concurrency_conflict` |
| 412 | Stale `If-Match` | `precondition_failed` |
| 422 | Well formed, but breaks a business rule | `event.capacity_mismatch`, `tier.unknown`, `idempotency_key_reused` |
| 429 | Per-user purchase rate limit | `rate_limited` (with `Retry-After`) |
| 500 | Unexpected; details are logged, never returned | `internal_error` |

## Why

- Clients can react differently: fix the form (400/422), refresh and retry (409/412), back off (429).
- Codes are stable contract; messages are for humans and can change.
- Someone else's order returns 404, not 403, so order ids cannot be probed for existence.

## Where rules live

- **FluentValidation** (Application): request shape, lengths, ranges, formats.
- **Domain**: business invariants that need the aggregate (capacity sums, sold vs capacity, state transitions). The domain throws `BusinessRuleViolationException` (422) or `DomainConflictException` (409) and knows nothing about HTTP; the mapping lives in the API layer.
