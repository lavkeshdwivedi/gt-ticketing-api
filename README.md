# Ticketing API

A REST API for a simplified event ticketing system: manage events with pricing tiers, sell tickets without ever overselling, and report sales. Built on .NET 10, ASP.NET Core, EF Core and SQL Server, with Clean Architecture and CQRS.

**What is proven, not just claimed:**

- **No overselling under contention.** 200 concurrent buyers racing for 50 seats sell exactly 50, and inventory counters, order rows, issued tickets and the sales report all reconcile. Tested against a real SQL Server and repeated to rule out flakiness; a k6 run of 2,000 attempts for 1,000 seats sells exactly 1,000.
- **Safe retries.** Purchases honour `Idempotency-Key`; 20 concurrent retries with the same key produce exactly one order.
- **No lost updates.** Admin edits use ETags; an edit based on a stale view (including stale inventory) gets `412`.
- **117 tests** across domain, application, architecture rules and full-stack integration; **97% line coverage** of hand-written code.
- **Measured performance.** A load test found the bottleneck the design predicted, and a targeted fix doubled hot-event throughput (`loadtest/RESULTS.md`).

---

## Contents

- [Run it](#run-it)
- [Try it](#try-it)
- [Run the tests](#run-the-tests)
- [API](#api)
- [Design](#design)
- [Consuming from Angular](#consuming-from-angular)
- [Assumptions](#assumptions)
- [With more time](#with-more-time)
- [Further reading](#further-reading)

## Run it

**Prerequisite:** Docker Desktop (or any Docker engine with Compose).

```bash
docker compose up --build
```

This starts three containers in order:

1. `sql`: SQL Server 2022, waits until healthy.
2. `migrate`: a one-shot EF Core migration bundle that creates or updates the schema, then exits.
3. `api`: the API on **http://localhost:8080**, started only after the migration succeeds.

Then open **http://localhost:8080/swagger**.

> Local development only: the compose file runs in `Development` mode, which enables a token endpoint for trying the API without an identity provider. The SQL password in it is for a throwaway local container.

### Without Docker for the API

With the .NET 10 SDK installed and the database from compose running (`docker compose up sql migrate`):

```bash
dotnet run --project src/Ticketing.Api          # http://localhost:5080/swagger
```

Schema changes during development: `dotnet tool restore`, then
`dotnet ef migrations add <Name> --project src/Ticketing.Infrastructure --startup-project src/Ticketing.Infrastructure`.

## Try it

**In Swagger:** call `POST /dev/token` with `{"subject": "admin-1", "roles": ["events.manage", "reports.read"]}`, copy `accessToken`, click **Authorize**, and paste it. A token with no roles is a regular buyer.

**With curl** (bash and [`jq`](https://jqlang.org)):

```bash
BASE=http://localhost:8080
ADMIN=$(curl -s -X POST $BASE/dev/token -H 'Content-Type: application/json' \
  -d '{"subject":"admin-1","roles":["events.manage","reports.read"]}' | jq -r .accessToken)
BUYER=$(curl -s -X POST $BASE/dev/token -H 'Content-Type: application/json' \
  -d '{"subject":"buyer-1"}' | jq -r .accessToken)

# Create an event: tier capacities must add up to totalCapacity. Timestamps need an explicit offset.
EVENT=$(curl -s -X POST $BASE/api/v1/events -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d '{
  "name": "Jazz Night", "description": "Quartet", "venue": "Blue Room",
  "startsAt": "2026-12-12T20:00:00-05:00", "currency": "USD", "totalCapacity": 100,
  "tiers": [ {"name": "VIP", "price": 120.00, "capacity": 20}, {"name": "GA", "price": 45.50, "capacity": 80} ] }')
EVENT_ID=$(echo "$EVENT" | jq -r .id); VIP=$(echo "$EVENT" | jq -r '.tiers[0].id')

# Buy two VIP tickets. Run it twice: the second call replays the same order (Idempotent-Replayed: true).
curl -si -X POST $BASE/api/v1/events/$EVENT_ID/purchases -H "Authorization: Bearer $BUYER" \
  -H 'Idempotency-Key: checkout-42' -H 'Content-Type: application/json' \
  -d "{\"tierId\":\"$VIP\",\"quantity\":2,\"customerName\":\"Ada\",\"customerEmail\":\"ada@example.com\"}"

curl -s $BASE/api/v1/events/$EVENT_ID/availability | jq                                 # 98 left, VIP 18
curl -s -X DELETE $BASE/api/v1/events/$EVENT_ID -H "Authorization: Bearer $ADMIN" | jq  # 409: has sales, cancel instead
curl -s $BASE/api/v1/events/$EVENT_ID/sales-summary -H "Authorization: Bearer $ADMIN" | jq
curl -s "$BASE/api/v1/reports/sales?from=2026-12-01T00:00:00Z&to=2027-01-01T00:00:00Z" -H "Authorization: Bearer $ADMIN" | jq
```

## Run the tests

```bash
dotnet test                                      # needs Docker running: integration tests start SQL Server
dotnet test --settings coverage.runsettings --collect:"XPlat Code Coverage"
```

| Project | Tests | What it covers |
|---|---|---|
| `Ticketing.Domain.Tests` | 43 | Every invariant and its boundary: exact sell-out, shrink to exactly sold, per-order limits, currency lock, soft delete vs sales, money arithmetic |
| `Ticketing.Application.Tests` | 23 | Handlers with substitutes: write-time sell-out rolls back, idempotent replay, key reuse with a different body, losing a same-key race, If-Match handling |
| `Ticketing.ArchitectureTests` | 7 | Dependencies point inwards; controllers never touch persistence; the API never references EF Core; infrastructure types stay internal |
| `Ticketing.Api.IntegrationTests` | 44 | The real app over HTTP against SQL Server in a container: CRUD, auth, every problem code, ETags, idempotency, rate limiting, reports, the overselling race, the raw SQL guard's every branch, Production hardening |

Coverage of hand-written code (generated code and migrations excluded): Application 100%, Infrastructure 96%, Domain 94%, API 100% (top-level `Program.cs` statements are compiler-generated and therefore excluded; they are exercised by every integration test). Overall 97%.

**Load test** (optional, against the compose stack with the purchase rate limit turned off):

```bash
PURCHASE_RATE_LIMIT_ENABLED=false docker compose up -d --build
docker run --rm -i --network gt-ticketing-api_default -e BASE_URL=http://api:8080 grafana/k6 run - < loadtest/purchase-load.js
```

(`gt-ticketing-api_default` is the compose network; it is named after the folder you cloned into.) Results and analysis: [`loadtest/RESULTS.md`](loadtest/RESULTS.md).

## API

Base path `/api/v1`. JSON in and out; enums as strings; money as decimals with an ISO 4217 currency; timestamps ISO 8601 **with an explicit offset**.

| Method | Path | Who | Notes |
|---|---|---|---|
| `GET` | `/events` | anyone | Paged (`page`, `pageSize` up to 100), filter by `from`, `to`, `status`, `search` |
| `GET` | `/events/{id}` | anyone | Returns `ETag`; `If-None-Match` gives `304` |
| `POST` | `/events` | `events.manage` | `201` + `Location` + `ETag` |
| `PUT` | `/events/{id}` | `events.manage` | Full replacement. Tiers matched by `id`; omit `id` to add a tier. `If-Match` gives `412` when stale |
| `DELETE` | `/events/{id}` | `events.manage` | Soft delete; `409` if tickets were sold |
| `POST` | `/events/{id}/cancel` | `events.manage` | Stops sales; idempotent |
| `GET` | `/events/{id}/availability` | anyone | Remaining seats per tier; `Cache-Control: no-store` |
| `POST` | `/events/{id}/purchases` | signed in | `201` + `Location: /orders/{id}`; optional `Idempotency-Key`; per-user rate limit (`429`) |
| `GET` | `/orders` | signed in | Your orders, newest first |
| `GET` | `/orders/{id}` | owner or `events.manage` | Someone else's order is `404`, not `403` |
| `GET` | `/events/{id}/sales-summary` | `reports.read` | Per-tier sold, remaining, revenue, sell-through |
| `GET` | `/reports/sales` | `reports.read` | Events starting in `[from, to)`, paged; totals **per currency** |

Health: `/health/live` (process) and `/health/ready` (database). OpenAPI document: `/openapi/v1.json`.

**Errors** are `application/problem+json` with a stable `code` and a `traceId`:

```json
{ "status": 409, "title": "Conflict", "code": "tickets.sold_out",
  "detail": "Only 1 tickets remain in tier 'General Admission'.", "traceId": "00-4bf9..." }
```

`400` bad shape (with per-field `errors`), `404`, `409` conflicts with current state, `412` stale `If-Match`, `422` business rule, `429` rate limited. Details in [ADR 0007](docs/adr/0007-error-model.md).

## Design

```
src/
  Ticketing.Domain          Aggregates and rules. No framework dependencies.
    Events/                 Event (aggregate root), PricingTier, PricingTierDefinition
    Orders/                 TicketOrder (price snapshot), Ticket (unique code per seat)
    Common/                 Money, DomainException types, SequentialGuid
  Ticketing.Application     Use cases: one handler per command or query, FluentValidation,
                            ports (IEventRepository, ITicketInventory, IUnitOfWork, read models)
  Ticketing.Infrastructure  EF Core + SQL Server: repositories, read models, the seat
                            reservation batch, migrations
  Ticketing.Api             Controllers, JWT auth, ProblemDetails, ETags, rate limiting
tests/                      Domain, Application, Architecture, Api.IntegrationTests
loadtest/                   k6 script and results
docs/adr/                   Architecture decision records
```

Dependencies point inwards (`Api -> Infrastructure -> Application -> Domain`), enforced by architecture tests.

### The decisions that matter

| Topic | Decision | ADR |
|---|---|---|
| **Overselling** | Inside one transaction: insert the order, then one SQL batch with two guarded `UPDATE`s (event still on sale, then `Sold + qty <= Capacity`). No read-modify-write window. A `CHECK` constraint backs it up. | [0001](docs/adr/0001-preventing-overselling.md) |
| **Admin edits racing sales** | Every purchase bumps the event's `rowversion`, so it is a true aggregate version: a delete, cancel or capacity change based on a stale view fails instead of racing a sale | [0001](docs/adr/0001-preventing-overselling.md), [0005](docs/adr/0005-concurrency-control-and-etags.md) |
| **Capacity model** | Tiers partition capacity; allocations must sum to `totalCapacity` | [0002](docs/adr/0002-tier-capacity-model.md) |
| **CQRS** | Commands go through the domain; queries project straight to DTOs. Handlers are called directly, no mediator | [0003](docs/adr/0003-cqrs-without-a-mediator.md) |
| **Retries** | `Idempotency-Key`, scoped per user, request fingerprinted; concurrent duplicates resolved by a unique index | [0004](docs/adr/0004-idempotent-purchases.md) |
| **Deletes** | Soft delete only without sales; otherwise cancel. Orders are financial records | [0006](docs/adr/0006-soft-delete-and-cancellation.md) |
| **Errors** | RFC 7807 with stable codes; 400 vs 422 vs 409 vs 412 | [0007](docs/adr/0007-error-model.md) |
| **Platform** | .NET 10 LTS (.NET 8 ends support 2026-11-10); licence-aware dependencies; decimal money; explicit-offset time | [0008](docs/adr/0008-platform-and-runtime.md) |
| **Auth** | JWT bearer, Entra-ready; public reads, authenticated purchases, role-based admin and reports | [0009](docs/adr/0009-authentication-and-authorization.md) |
| **Schema** | Migrations ship as a bundle run at deploy time; the API never touches schema | [0010](docs/adr/0010-schema-migrations-at-deploy-time.md) |

### Data model

```
Events        Id, Name, Description, Venue, StartsAt (datetimeoffset), Currency, TotalCapacity,
              Status, CreatedAt, UpdatedAt, IsDeleted, DeletedAt, LastSoldAt, Version (rowversion)
PricingTiers  Id, EventId -> Events, Name, Price, Currency, Capacity, Sold
              CHECK (Sold >= 0 AND Sold <= Capacity)
TicketOrders  Id, EventId, PricingTierId (both ON DELETE RESTRICT), TierName, Quantity, UnitPrice,
              Currency, CustomerName, CustomerEmail, PurchasedBy, IdempotencyKey, RequestFingerprint,
              PurchasedAt.  UNIQUE (PurchasedBy, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL
Tickets       Id, OrderId -> TicketOrders, Code (UNIQUE, unambiguous alphabet)
```

Orders snapshot the tier name and unit price, so repricing a tier never rewrites what customers paid; reports use those snapshots. Order totals are derived (`UnitPrice x Quantity`), never stored. Keys are sequential GUIDs, so inserts append to the clustered index.

### Scalability

The API is stateless and scales horizontally; the database is the limit. Purchases for one event serialize on a short row lock (the price of race-free admin edits); purchases for different events do not interact. On a laptop that is about 158 purchases per second on a single hot event and 900+ spread across events. The path beyond that (read replica for reports, edge caching, Service Bus queue-based allocation for flash sales, outbox for integration events) is in [`docs/operationalizing.md`](docs/operationalizing.md).

## Consuming from Angular

There is deliberately no UI in this repository; the brief asks for an API. This is how an Angular 22 front end would consume it. The snippets are illustrative and are not part of the tested deliverable.

**Typed client from the OpenAPI document**, regenerated in CI whenever the contract changes, for example:

```bash
npx @openapitools/openapi-generator-cli generate -g typescript-angular \
  -i http://localhost:8080/openapi/v1.json -o src/app/api
```

**Purchases: one idempotency key per checkout, reused by every retry.** Generating the key in an interceptor would be wrong, because a retry would get a new key and could buy twice.

```ts
@Injectable({ providedIn: 'root' })
export class TicketingService {
  private readonly http = inject(HttpClient);

  purchase(eventId: string, body: PurchaseTicketsRequest): Observable<OrderDto> {
    const headers = new HttpHeaders({ 'Idempotency-Key': crypto.randomUUID() });
    return this.http.post<OrderDto>(`/api/v1/events/${eventId}/purchases`, body, { headers }).pipe(
      // Network blips and gateway errors are safe to retry: the server replays the original order.
      retry({ count: 3, delay: (error, attempt) => isTransient(error) ? timer(250 * 2 ** attempt) : throwError(() => error) }),
    );
  }

  updateEvent(current: EventDto, changes: UpdateEventRequest): Observable<EventDto> {
    // 412 means someone else changed it: reload and let the user re-apply their edit.
    return this.http.put<EventDto>(`/api/v1/events/${current.id}`, changes, { headers: { 'If-Match': `"${current.version}"` } });
  }
}

const isTransient = (e: HttpErrorResponse) => e.status === 0 || e.status === 502 || e.status === 503 || e.status === 504;
```

**Errors:** a functional `HttpInterceptorFn` maps `application/problem+json` to user messages by `code` (`tickets.sold_out`: refresh availability and suggest another tier; `rate_limited`: honour `Retry-After`; `validation_failed`: bind `errors` to form fields).

**Auth:** `@azure/msal-angular`'s `MsalInterceptor` attaches Entra ID tokens for `/api/*`; route guards check the `events.manage` and `reports.read` app roles for the admin screens (the API enforces them regardless).

**Availability:** a signal refreshed on an interval while the event page is open (`toSignal(timer(0, 5000).pipe(switchMap(() => http.get<AvailabilityDto>(...))))`); the endpoint is `no-store`, so it is never served stale.

## Assumptions

- Tiers partition capacity (ADR 0002); up to 10 tiers per event and 10 tickets per order.
- One currency per event; reports never add currencies together.
- Customer name and email are taken from the request (the ticket holder may differ from the signed-in buyer).
- Purchases are confirmed immediately: there is no payment step, hold or expiry in this scope.
- Events cannot be edited after they start; price changes apply to future sales only.
- A cancelled event keeps its orders; refunds would be handled by a payments service (see operationalizing).

## With more time

1. **Transactional outbox + Service Bus** for `TicketsPurchased` and `EventCancelled` (refunds, emails, reporting projections).
2. **Seat holds with expiry** tied to a payment step, and queue-based allocation for flash sales (design in ADR 0001).
3. **Require `If-Match`** on `PUT` (`428` when missing) and require `Idempotency-Key` for first-party clients.
4. **Pagination on `GET /orders`** (currently capped at the 100 most recent) and cursor-based paging for large reports.
5. **OpenTelemetry** traces and business metrics (sold-out rate, conflict rate, lock wait) into Application Insights.
6. **Contract tests** that fail CI when the OpenAPI document changes incompatibly.
7. **Idempotency key retention** window and cleanup job.

## Further reading

- [`docs/adr/`](docs/adr): ten short decision records
- [`docs/operationalizing.md`](docs/operationalizing.md): Azure deployment, identity, pipeline, scaling, observability, monolith-to-services seams
- [`loadtest/RESULTS.md`](loadtest/RESULTS.md): measurements and what changed because of them
- [`AI_USAGE.md`](AI_USAGE.md): how AI was used, what it got wrong, and how that was caught
