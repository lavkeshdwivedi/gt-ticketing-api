# ADR 0004: Idempotent purchases with an Idempotency-Key header

Status: Accepted

## Context

A client that times out on "buy tickets" does not know whether the purchase happened. If it retries blindly it may buy twice; if it gives up it may think it failed when it did not. In payments this is solved with idempotency keys, and a ticket purchase is a payment-shaped operation.

## Decision

`POST /api/v1/events/{id}/purchases` accepts an optional `Idempotency-Key` header (the same model Stripe uses):

- First request with a key: processed normally; the key and a SHA-256 fingerprint of the request body are stored on the order.
- Same key, same body: returns the original order with `201` and `Idempotent-Replayed: true`. No seats are reserved again.
- Same key, different body: `422 idempotency_key_reused`. That is a client bug and must not be papered over.
- Keys are scoped per user (unique index on `PurchasedBy, IdempotencyKey`), so one customer's key can never collide with, or reveal, another customer's order.
- Two concurrent requests with the same key: both may pass the initial lookup, but only one insert can satisfy the unique index. The loser's transaction rolls back (releasing its seats) and it returns the winner's order. An integration test fires 20 concurrent retries and asserts exactly one order.

## Alternatives considered

- **Required header.** Stronger guarantee, but friction for reviewers and simple clients. In production for a mobile client I would make it required.
- **Separate idempotency store (cache or table) storing full responses.** More general (works for any endpoint), but more moving parts. Storing the key on the order itself is atomic with the order by construction.

## Consequences

- Keys never expire in this implementation. In production they would be retained for a window (for example 24 hours) and cleaned up.
