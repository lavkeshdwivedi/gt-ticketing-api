# ADR 0003: CQRS with explicit handlers, no mediator

Status: Accepted

## Context

The target platform uses Clean Architecture with MediatR for CQRS. This service has twelve use cases. MediatR 13 and later also moved to a commercial license, which makes adopting it a procurement decision as well as a technical one.

## Decision

Keep the CQRS split, drop the mediator:

- One handler class per use case: `CreateEventCommandHandler`, `PurchaseTicketsCommandHandler`, `GetSalesReportQueryHandler`, and so on.
- Controllers receive the handler they need via `[FromServices]` and call `HandleAsync` directly.
- Validation is an explicit first line in each handler (`validator.ValidateAndThrowAsync`), not a hidden pipeline behavior.
- **Commands** load aggregates through repositories and enforce rules in the domain.
- **Queries** never touch aggregates. They go through read-model interfaces (`IEventReadModel`, `ISalesReportReadModel`) implemented with `AsNoTracking` projections straight into DTOs.

## Why

- Go-to-definition from a controller lands on the code that runs. No indirection to explain.
- With about a dozen handlers, a mediator's main benefit (cross-cutting pipeline behaviors) does not pay for itself yet.
- The read/write split is where CQRS earns its keep here: reporting queries are shaped for reading, and the write side stays a rich domain model.

## When I would add MediatR

When cross-cutting concerns multiply across many handlers (validation, logging, metrics, transactions, authorization per request type), a pipeline removes real duplication. Moving to it is mechanical: each handler already has one entry point and one request type. MediatR 12.5 is the last Apache-2.0 release if licensing is a concern.
