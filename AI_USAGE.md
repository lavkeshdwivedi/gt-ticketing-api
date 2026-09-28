# How I used AI on this exercise

I built this with Claude Code (Anthropic's coding agent) working in my terminal. The short version: I acted as tech lead and reviewer, the agent did most of the typing, and nothing was accepted without tests proving it, including against a real SQL Server.

## How the work was split

**I owned the decisions.** Before and during the build, I had the agent bring every design choice to me as a small set of options with trade-offs and a recommendation, and I picked. The ones that shaped the system:

| Decision | What I chose | Why |
|---|---|---|
| How to prevent overselling | Guarded atomic `UPDATE`, not optimistic retry or pessimistic locks | No retry storms during an on-sale; the database enforces the invariant |
| Delete/cancel racing a purchase | Every purchase bumps the event's rowversion | Makes admin edits provably safe; cost measured later |
| Tier model | Tiers partition total capacity | How venues actually sell; per-tier guard also guards the event |
| Duplicate purchases | Optional `Idempotency-Key`, scoped per user | Payment-style retries without double charges |
| Lost updates | ETag / `If-Match` on `PUT` | Standard HTTP, cheap |
| MediatR | Left out, kept CQRS handlers | A dozen handlers do not need a mediator (ADR 0003) |
| Status codes | 400 shape / 422 rules / 409 state | Clients can react differently to each |
| Auth | Entra-ready JWT, role policies, dev-only token endpoint | Realistic without an identity provider on a reviewer's laptop |
| Runtime | .NET 10 LTS instead of .NET 8 | .NET 8 is six weeks from end of support |
| Angular UI | Not built; documented the client integration instead | The brief grades the API; a thin UI would dilute it |
| Migrations | Out of the API, into a one-shot migrator | Production-correct, and keeps the API clean |
| Hot path | Shorten the lock window after measuring it | Load test showed the bottleneck; fix doubled throughput |

**The agent implemented in small slices**, each one building and tested before the next: domain, application, persistence, API, then tests at every level. The git history is in that order.

## Where it accelerated me

- Scaffolding the solution, central package management, analyzers and warnings-as-errors in minutes.
- Writing the bulk of the tests. 117 tests (domain, handlers, architecture rules, and Testcontainers integration tests including a 200-buyer race) is not something I would have had time to write by hand in a few hours.
- Keeping docs honest: ADRs were written alongside the code, and updated when the code changed (ADR 0001 was revised after the load test).
- Checking current facts instead of guessing: current .NET support dates, current package versions, which libraries changed licence.

## Where it was wrong, and how it was caught

I think this is the most important part. AI output is a draft until something independent proves it.

1. **A hung concurrency test.** The first version of the 200-buyer test awaited all purchases before opening the start gate, so it deadlocked. The test run timed out; the fix was an async gate (`TaskCompletionSource`) instead of a blocking one.
2. **A Linux-only crash the tests could not see.** The `webapi` template enables `InvariantGlobalization`. On Linux, SqlClient needs the `en-US` culture, so the container crashed on its first database call. All tests passed on Windows. It was caught by running the real container end to end rather than trusting green tests, and fixed by removing the setting.
3. **A query EF could not translate.** An early read model used an `AsQueryable()` trick inside a projection. It was spotted in review before running; the ownership check was redesigned into the `WHERE` clause, which is also better security (a foreign order is indistinguishable from a missing one).
4. **A wrong architecture rule.** A NetArchTest filter chained `Or()` in a way that grouped differently than intended and flagged correct code. Rewritten as plain reflection so the rule is unambiguous.
5. **Misleading numbers, twice.** Coverage first read 75% because source generators (OpenAPI XML docs, regex, logging) were counted; with generated code excluded it is 97% of hand-written code, and the README notes the one caveat (top-level `Program.cs` is excluded too). The first load test reported 0.36% failures that were actually setup calls returning 200. Both are fixed and explained rather than hidden.
6. **Three more from a deliberate review pass.** After everything was green I had the agent re-read the purchase path and error handling looking for failure modes. It found: an admin removing an unsold tier while a buyer purchases from it surfaced as a 500 (foreign key violation), now a 409 retry; malformed JSON returned a 400 without the stable `code` every other error has; and the controllers' `[Produces]` attributes were silently turning error bodies into `application/json`. That last one was caught only because the new test asserted the content type. Each fix came with a test.
7. **A race found during design, not by a test.** Delete versus purchase could delete an event with a sale on it. The agent flagged it while implementing the delete path and laid out three ways to close it; I chose the fix.

## How I verified

- Every behaviour claimed in the README has a test, and the concurrency tests run against real SQL Server, not an in-memory fake.
- The overselling tests were run five times in a row to rule out flakiness.
- The full stack was run from scratch with docker compose and exercised with plain `curl`, and load tested with k6.
- I reviewed the code myself, with particular attention to the purchase transaction, the one raw SQL batch, the auth rules and the error mapping.

## What I would keep doing on a team

- Have the agent propose options and trade-offs; keep the decision human and write it down (ADRs).
- Treat generated code as untrusted until an independent check (a test, a real container, a measurement) agrees with it.
- Expect the agent to look for failure modes in its own work (races, edge cases), and make it show them rather than quietly patch them. Here it surfaced a real race.
