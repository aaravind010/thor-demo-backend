# Thor.Core

Shared cross-cutting infrastructure for Thor services (Thor.Api, Thor.TaskApi,
and future workflow modules) — the common building blocks every service needs
that don't belong to any one service's business logic. Consumed via
`<ProjectReference>`, same as the other `backend/shared/*` libraries.

Each concern lives in its own namespace/folder under this project (e.g.
`Thor.Core.Logging`) and can be adopted independently — referencing Thor.Core
for one module doesn't pull in the others. Logging is the first module;
more will land here over time.

## Logging (`Thor.Core.Logging`)

Shared Serilog logging bootstrap. It has no notion of tenancy, Master DB, or
any specific enrichment key — it only knows about `LogContext` (Serilog's
ambient property stack) and an `ITenantLogSinkResolver` *interface*. Callers
decide what context to attach and whether per-tenant sink routing is wanted at
all — see [ADR §10.1](../../../docs/architecture/ADR.md).

### Bootstrapping a service

```csharp
builder.Host.UseThorLogging();
```

This wires Serilog as the logging provider, reading sinks/format/level entirely
from that service's own `"Serilog"` appsettings section
(`Serilog.Settings.Configuration` — no format or sink is hard-coded here), and
enriches every log event with whatever is currently pushed onto `LogContext`.

### Attaching context to log statements

- **`HeaderLogEnrichmentMiddleware`** — takes a `Dictionary<string, string>` mapping
  request header name → LogContext property name, so each service decides its own
  header names:
  ```csharp
  app.UseMiddleware<HeaderLogEnrichmentMiddleware>(new Dictionary<string, string>
  {
      ["X-THOR-TENANT-ID"] = "TenantId",
  });
  app.UseSerilogRequestLogging();
  ```
  Must be registered **before** `UseSerilogRequestLogging()` — middleware wraps
  inside-out, and the pushed property needs to still be on the stack when the
  request-completion line logs on the way back out.
- **`ThorLogContext.PushProperty` / `PushProperties`** — for anything not on a
  header (e.g. values from the request body). Push it as close to the HTTP
  boundary as possible (controller, not service) so business logic stays a plain
  `ILogger<T>` consumer with no dependency on this project.

A property absent from a given log event's template just renders empty — safe
for workflows to enrich with `ScanId` instead of `TenantId`, using the exact same
mechanism.

### Per-tenant HTTP sink (opt-in)

A service that wants each tenant's logs shipped to its own external HTTP
endpoint registers a resolver in DI:

```csharp
builder.Services.AddSingleton<ITenantLogSinkResolver, TenantLogSinkResolver>(); // Thor.DataConnectionManager
```

If nothing is registered, `UseThorLogging()` skips this entirely — services with
no need for it (or no Master DB access) pay no cost. When registered:

- Serilog.Sinks.Map routes each event to a sink keyed by its `TenantId` property.
- The whole thing is wrapped in `WriteTo.Async`, so resolving the tenant's
  endpoint (a cached Master DB lookup) and the HTTP dispatch itself both happen
  on a background thread — the calling thread only enqueues and returns.
- Delivery is **batched**, not one request per log line: up to 1000 events or
  ~2s, whichever comes first (Serilog.Sinks.Http defaults). Override per tenant
  via `TenantLogSinkOptions.BatchSizeLimit`, resolved from
  `auth.tenant_log_sink_config` in the Master DB.
- A tenant with no configured sink just falls back to the service's other sinks
  (console) — absence is a normal state, not an error.

The concrete resolver lives in `Thor.DataConnectionManager` (it needs Master DB
access), not here — this project only owns the `ITenantLogSinkResolver`
contract, keeping it free of any EF Core/Postgres dependency.

### Logging conventions

- Use the level that matches the event: `Information` for normal meaningful
  events, `Warning` for expected-but-notable outcomes (e.g. a tenant-resolution
  mismatch), `Error`/`Fatal` for unhandled failures. Don't swallow an exception
  without logging it.
- Always log via message templates (`logger.LogInformation("... {Foo}", foo)`),
  never string interpolation (`$"..."`) — a filtered-out level then costs
  nothing, since the template arguments are never evaluated or formatted.
- Keep `Debug`/`Verbose` for detail only needed while diagnosing something; the
  per-service `MinimumLevel` in `appsettings.json` is what keeps normal
  production logging cheap.
