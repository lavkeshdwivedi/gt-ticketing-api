# Operationalizing on Azure

How I would take this service from "runs in docker compose" to production, using the platform's existing building blocks (Azure, Entra ID, Service Bus, Key Vault, Azure DevOps or Octopus).

## Target shape

```
             Azure Front Door (WAF, TLS, per-IP rate limits)
                          |
                 API Management (optional: products, quotas, API keys for partners)
                          |
   Azure Container Apps / AKS  ->  Ticketing API  (N stateless replicas, managed identity)
                          |                  |
                  Azure SQL Database     Service Bus  (TicketsPurchased, EventCancelled)
                   (zone redundant)          |
                          |              Reporting / notifications / payments consumers
                  read replica  <- sales reports (ApplicationIntent=ReadOnly)

   Key Vault (non-identity secrets) . Entra ID (users, app roles) . Application Insights (OpenTelemetry)
```

## Identity and secrets

- **Users and roles**: Entra ID app registration with app roles `events.manage` and `reports.read`. Set `Auth:Authority` and `Auth:Audience`; the code already validates Entra tokens and reads the `roles` and `oid` claims (ADR 0009). The dev token endpoint cannot be mapped outside Development and the app refuses to start without an authority.
- **Database access without a password**: the API's managed identity is a contained database user with data rights only (`db_datareader`, `db_datawriter`). Connection string uses `Authentication=Active Directory Managed Identity`. There is no SQL password to store or rotate.
- **Schema changes** run from the pipeline under a separate identity that has DDL rights (ADR 0010). The API never can.
- **Key Vault** holds anything that is not identity based (for example a payment provider key later), loaded through the Key Vault configuration provider with the same managed identity.

## Deployment pipeline

Azure DevOps (see `azure-pipelines.yml`) or Octopus Deploy:

1. **Build and test**: warnings-as-errors build, migrations-in-sync check, unit, architecture and Testcontainers integration tests, coverage.
2. **Images**: build and push the `runtime` and `migrator` images to Azure Container Registry, tagged with the commit SHA.
3. **Migrate**: run the migrator image as a one-off job against the target database. Migrations follow expand, migrate, contract so the currently running API keeps working.
4. **Deploy**: roll out the API image (Container Apps revisions or a Kubernetes rolling update), gated on `/health/ready`. Keep the previous revision for instant rollback.
5. **Promote** through dev, test, prod with the same images; only configuration changes between environments.

## Reliability

- **Health probes**: `/health/live` (process is up, no dependencies) for liveness, `/health/ready` (database reachable) for readiness, so a replica that loses the database stops receiving traffic instead of being restarted in a loop.
- **Transient faults**: EF Core's SQL Server execution strategy retries transient Azure SQL errors, and user transactions run inside it so the whole purchase is retried as a unit, never half of it.
- **Retries from clients** are safe because purchases honour `Idempotency-Key` (ADR 0004).
- **Database**: Azure SQL zone-redundant, point-in-time restore, and a failover group to a paired region if the business needs regional DR.

## Scaling

- The API is stateless; scale replicas on CPU and request rate.
- **The database is the real limit**, specifically the per-event lock on a popular on-sale (ADR 0001, `loadtest/RESULTS.md`). In order of cost:
  1. Keep the API and database in the same region and zone; the lock is held for about one round trip.
  2. Move reports to a read replica (`ApplicationIntent=ReadOnly`) so analytics never compete with purchases.
  3. Cache `GET /events` and event details at the edge or in Redis for a few seconds; availability stays `no-store`.
  4. For flash sales, switch that event to queue-based allocation: purchase requests go to a Service Bus session keyed by event id, a single consumer allocates seats in order with short holds that expire unless payment confirms. The client gets `202 Accepted` and polls or receives a notification.
- **Rate limiting**: the per-user limiter in the API (429 with `Retry-After`) plus per-IP limits and bot protection at Front Door.

## Integration events (the next thing I would build)

Other systems care when tickets sell or an event is cancelled (payments, email, reporting). Publishing straight to Service Bus inside the purchase would risk "order committed, message lost" or the reverse, so I would use a **transactional outbox**:

1. In the same transaction as the order, insert an `OutboxMessages` row (`TicketsPurchased { orderId, eventId, tierId, quantity, total }`).
2. A background worker (or Azure Function) reads unsent rows, publishes to a Service Bus topic, marks them sent. Consumers are idempotent on message id.
3. `EventCancelled` triggers refunds in the payments service.

## Observability

- OpenTelemetry traces, metrics and logs to Application Insights. The `traceId` already returned in every problem response lets support jump from a customer's error to the trace.
- Business metrics worth alerting on: purchases per minute, `tickets.sold_out` responses, `concurrency_conflict` and `precondition_failed` rates, 429s, purchase p95 latency, SQL lock wait time on `Events`.
- Structured log fields: event id, tier id, order id, caller id (never the email).

## Data protection

- Customer name and email are personal data: encrypted at rest (TDE), excluded from logs, and subject to a retention policy. Ticket codes are random, unguessable, and not derived from personal data.
- Orders are financial records: they are never physically deleted by the API (ADR 0006).

## From monolith to services

This codebase is already split along the seams I would use if it were carved out of a monolith with the strangler pattern:

| Module | Owns | Could become |
|---|---|---|
| Events (catalog) | events, tiers, pricing | Catalog service |
| Orders and inventory | seat counters, orders, tickets | Ticketing service (the transactional core) |
| Reports | read models | Reporting service fed by `TicketsPurchased` events, with its own store |

The read side already goes through its own interfaces (`IEventReadModel`, `ISalesReportReadModel`), so pointing reports at a separate store fed by integration events is a change behind an interface, not a rewrite. I would split only when a module needs to scale or deploy independently; until then a well-structured modular service is cheaper to run.
