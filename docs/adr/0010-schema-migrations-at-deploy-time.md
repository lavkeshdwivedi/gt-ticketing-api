# ADR 0010: Schema migrations run at deploy time, never from the API

Status: Accepted

## Context

A common shortcut is calling `Database.Migrate()` when the app starts. It works on a laptop and causes trouble in production: several instances start at once and race to change the schema, the app's database login needs DDL rights it should never have, a failed migration takes the whole app down mid-rollout, and schema changes become invisible side effects of a deploy instead of a reviewed step.

## Decision

- The API contains no schema management. An architecture test fails the build if the API assembly ever references EF Core directly.
- EF Core migrations live in `Ticketing.Infrastructure/Persistence/Migrations` as the versioned history of the schema.
- They ship as an **EF Core migration bundle**: a single executable, built in the `migrator` stage of the Dockerfile, that applies pending migrations and exits.
- Locally, docker compose runs `migrate` once; the API only starts after it exits successfully.
- In a pipeline, the bundle runs as its own deployment step (an Azure DevOps or Octopus step, or a Kubernetes Job) with a login that has DDL rights. The API's managed identity gets data rights only.
- Integration tests apply the same migrations in their fixture, so tests exercise the real schema, constraints included.

## Consequences

- Migrations must be backward compatible with the currently running version of the API (expand, migrate, contract), because the new schema lands before the new code.
- For DBA review, `dotnet ef migrations script --idempotent` produces the exact SQL the bundle will run.
