# ADR 0008: .NET 10 LTS, SQL Server, and licence-aware dependencies

Status: Accepted

## Runtime

The target platform lists .NET 8. As of September 2026, .NET 8 (and .NET 9) reach end of support on 10 November 2026. Starting new code on a runtime six weeks from end of support is hard to justify, so this service targets **.NET 10, the current LTS (supported to November 2028)**.

Nothing in the design depends on .NET 10 features. The service was first built on .NET 8 and upgraded; the upgrade was package bumps plus moving from Swashbuckle's generator to the built-in `Microsoft.AspNetCore.OpenApi` document. The same path applies to the wider platform, and it is worth planning before November.

## Database

SQL Server (Azure SQL in production), via EF Core 10:

- The workload is relational and transactional: inventory, orders and money must change together or not at all.
- It gives the primitives the design relies on: guarded atomic `UPDATE`, `rowversion`, filtered unique indexes, `CHECK` constraints.
- It is the platform's existing database, so operations, backup and skills already exist.

## Dependencies chosen with licensing in mind

Several popular .NET libraries changed licence recently. This matters for a company shipping commercial software:

| Library | Situation | Choice |
|---|---|---|
| MediatR | 13+ commercial | Not used (ADR 0003) |
| AutoMapper | 15+ commercial | Not used; mapping is explicit |
| FluentAssertions | 8+ commercial | Shouldly (MIT) |
| FluentValidation | Apache 2.0 | Used |
| Swashbuckle | Generator replaced by built-in OpenAPI | Only its Swagger UI package is used |

## Other conventions

- **Money** is `decimal` plus an ISO 4217 currency (`Money` value object). Arithmetic across currencies throws. Reports total each currency separately.
- **Time** is `DateTimeOffset`. Request timestamps must carry an explicit offset (`Z` or `+hh:mm`); a bare local time is rejected with 400, because by default it would be interpreted in the server's time zone and silently move the event. All "now" reads go through `TimeProvider` so tests control the clock.
- **Order total** is derived (`UnitPrice x Quantity`), not stored, so it can never disagree with its inputs.
- **Identifiers** are sequential GUIDs generated in the domain, so inserts append to SQL Server's clustered index rather than splitting random pages.
