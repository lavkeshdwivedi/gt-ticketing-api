# Load test results

Measured with `loadtest/purchase-load.js` (k6 in Docker) against the docker compose stack on a single developer laptop (API and SQL Server 2022 in containers under WSL2, purchase rate limit disabled). Absolute numbers will differ on real infrastructure; the comparisons are the point.

## What was measured

| Scenario | Load | Question |
|---|---|---|
| `hot_event` | 50 concurrent buyers, 30 s, all on **one** event | What does per-event serialization (ADR 0001) cost? |
| `many_events` | 50 concurrent buyers, 30 s, spread over **20** events | Do different events scale independently? |
| `sell_out` | 100 buyers x 20 attempts = 2,000 tries for 1,000 seats | Is the result exact under contention? |

## Results

| Scenario | v1: EF, 4 round trips under lock | v2: order first, one-batch guard | Change |
|---|---|---|---|
| `hot_event` p50 / p95 | 601 ms / 662 ms | **316 ms / 487 ms** | ~1.9x throughput (~80 to ~158 purchases/s) |
| `many_events` p50 / p95 | 56 ms / 120 ms | 51 ms / 82 ms | Unchanged (not lock bound) |
| `sell_out` | 1,000 sold, 0 remaining, 1,000 orders | 1,000 sold, 0 remaining, 1,000 orders | Exact both times |
| Failed requests | 0 | 0 | |

## Reading the numbers

- Spreading the same load over 20 events is about 6x faster than piling it on one. That gap is the price of the event row lock that makes admin edits race-free (ADR 0001). It was expected; the load test put a number on it.
- The first version held that lock across four database round trips (event update, tier update, order insert, commit). Inserting the order before taking the lock and sending both guarded updates as one batch cut the hold time to about one round trip plus commit, which roughly doubled hot-event throughput with no change to correctness.
- Correctness never moved: in every run, tier counters, order rows, issued tickets and the sales report agree, and nothing is oversold.

## Next steps if one event needs far more than this

1. Put the database closer (same VNet/zone) and on real hardware: the remaining cost is mostly network round trip and commit latency.
2. Queue-based allocation: purchases for an event go onto an Azure Service Bus session keyed by event id, and one consumer per session allocates seats in order with short-lived holds that expire unless payment confirms. That turns a lock queue into a work queue, adds fairness, and absorbs spikes.
3. Pre-partitioned inventory (seat blocks per shard) if a single event needs more than one consumer can allocate.
