# ADR 0002: Pricing tiers partition the event's capacity

Status: Accepted

## Context

The brief asks for both a "total ticket capacity" and "pricing tiers" on an event without saying how they relate. There are two reasonable readings:

1. Tiers are just price points drawing from one shared pool.
2. Each tier has its own allocation (VIP: 50, General Admission: 450) and the allocations make up the total.

## Decision

Tiers partition capacity. Each tier has its own `Capacity`, and the domain enforces `sum(tier capacities) == totalCapacity` on create and update.

## Why

- It matches how real venues sell: you cannot sell 500 VIP seats because GA is empty.
- Overselling is prevented per tier, and because tiers sum to the total, the event can never be oversold either. No second event-level counter to keep consistent.
- `totalCapacity` stays in the API because the brief asks for it, and it doubles as a cross-check that catches client arithmetic mistakes (422 `event.capacity_mismatch`).

## Consequences

- Moving seats between tiers is an explicit admin edit.
- A tier cannot shrink below what it has sold, and a tier with sales cannot be removed (409).
- If a shared pool is ever needed (for example "any seat, dynamic price"), that is a different inventory model, not a flag on this one.
