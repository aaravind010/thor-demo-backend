# ADR: Connector Credential (Authentication-Method) Secret Management

> Extends [`ADR.md`](./ADR.md) §8 (Secrets & Encryption). See also §6.2 ("credentials live in
> Secrets Manager") and §5.2 (no-plaintext-credential precedent), which this follows.

## 1. Problem

`POST /authentication-methods` lets a tenant register a named authentication method (e.g. an
API key or OAuth credential for a connector) with one or more field values (`client_id`,
`client_secret`, etc., persisted as `AuthenticationValue` rows). These field values were
stored as **plaintext** in the per-tenant Postgres database — no encryption-at-rest marking,
no secret handling at all. That's inconsistent with the platform's existing bar: tenant DB
credentials are never stored in a database (§6.2), and API keys are never stored reversibly
(§5.2).

## 2. Solution

Field values now live in **AWS Secrets Manager**, not Postgres. `authentication_values.secret_arn` stores the arn of the secret manager. Postgres keeps only a pointer, zero secret bytes.

One secret is shared by every `AuthenticationValue` for a given **tenant + authentication
type** (not one secret per value or per method), named:

```
tenant/<tenantId>/<authenticationTypeId>
```

`authenticationTypeId` is `AuthenticationType.Id` (Master DB) rather than its `Name` or
`ConnectorType` — those are mutable display strings, and a rename must not orphan an existing
secret.

## 3. Storage structure and write logic

Each secret is one JSON object, one entry per `AuthenticationValue` row, keyed by that row's
own `Id` (not `FieldId` — `FieldId` repeats across every method of the same type, e.g. two
Salesforce connections both have a `client_secret` field):

```json
{
  "values": {
    "<authenticationValueId>": "<secret-value>"
  }
}
```

**Batched, single write per request.** A request has exactly one `TypeId`, so every field
value in it belongs to the same secret. All of them are merged into one in-memory payload and
written with a single get-then-put (`GetSecretValue` → merge → `PutSecretValue`, or
`CreateSecret` if the secret doesn't exist yet) — not one round trip per field.

This is a correctness fix, not just an efficiency one: **Secrets Manager does not guarantee
read-after-write consistency** — `GetSecretValue` can briefly return a stale copy immediately
after a `PutSecretValue`/`CreateSecret` on the same secret, per AWS's own documented
propagation delay. Writing one field per round trip left a window, per field, where a later
`Get` could read a copy that predated an earlier `Put` from the same request; since `Put`
replaces the whole blob, the earlier field's entry would be silently dropped — with no
exception anywhere, since every individual AWS call succeeds. This was confirmed in practice
(not merely theoretical) and reproduced with fully sequential, single-caller requests, no
application-level overlap required. Collapsing every field into one `Get` + one `Put` per
request removes this risk entirely for fields within the same request.

**Postgres advisory lock (cross-instance).** Batching removes the race within a request; the
same race could still occur *across* separate requests for the same tenant+type, including
from a different Thor.Api/ECS task — an in-process lock can't prevent that. Instead,
`AuthenticationMethodService.CreateAsync` opens an explicit transaction on the tenant DB
connection it already holds and takes `pg_advisory_xact_lock(hashtext(secretName))` as its
first statement, before calling `IAuthenticationSecretWriter`. The lock is held by Postgres —
not by this process — for the transaction's lifetime and is released automatically on commit
or rollback, so any caller anywhere serializes on it, not just callers in the same instance.
The secret name is defined once (`AuthenticationSecretNaming`) and used for both the lock key
and the actual Secrets Manager name, so the two can't drift apart. `hashtext` collisions are
possible (32-bit hash) but only cause harmless over-serialization between unrelated
tenant+type pairs, never under-locking.
