# ADR 0009: JWT bearer auth with role policies

Status: Accepted

## Decision

| Who | Can |
|---|---|
| Anonymous | Browse events and availability |
| Any signed-in user | Buy tickets; read and list **their own** orders |
| Role `events.manage` | Create, update, cancel, delete events; read any order |
| Role `reports.read` (or `events.manage`) | Sales summaries and reports |

- Tokens are validated as JWT bearer tokens. In production `Auth:Authority` points at an OpenID Connect provider (Entra ID); roles come from the `roles` claim (Entra app roles) and the user id from `oid` or `sub`.
- Order ownership is enforced inside the query (`WHERE Id = @id AND PurchasedBy = @caller`), so a foreign order is indistinguishable from a missing one (404, no IDOR).

## Local development

`POST /dev/token` mints a short-lived token signed with a local key, so reviewers can try every role from Swagger without an identity provider. It is guarded three ways, each covered by a test:

1. Only mapped when the environment is `Development` and `Auth:EnableDevTokenEndpoint` is true.
2. Never mapped when a real `Authority` is configured.
3. Outside Development the app refuses to start without an `Authority`, so it can never silently fall back to the development key.

## Not done here

Customer identity and purchase history would normally come from an identity service; name and email are taken from the request because the ticket holder may differ from the buyer.
