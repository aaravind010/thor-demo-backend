# Thor Backend

Thor is a **multi-tenant Identity Hygiene SaaS platform**, cloud-native and
deployed on AWS. This repository contains code for Backend and Infrastructure. Frontend + Connector code lives in a separate repository. 


Full design: `docs/architecture/ADR.md` (authoritative). Read it before non-trivial work.

## Stack
- **Services / functions:** C# / .NET 10 (Thor API, Task API, workflow modules, Lambda authorizer)
- **Intelligence Engine:** Python + LangChain + AWS Bedrock
- **Data:** Aurora PostgreSQL Serverless v2 (EF Core + Npgsql), Neptune (Gremlin.Net)
- **Infra:** Terraform. Everything dockerized.

## Layout
```
backend/services/                       long-running APIs: Thor.Api (internet-facing), Thor.TaskApi
backend/functions/                      .NET Lambdas: Thor.Authorizer (REST API REQUEST authorizer;
                                        verifies hashed API keys against Master DB — see ADR §5)
backend/workflows/                      business-process modules; Abstractions = shared base,
                                        each module is its own executable + Dockerfile
backedn/shared/                         MultiTenancy, Data, Graph, Auth, Contracts, Common
backend/services/intelligence-engine/   Python (self-contained: pyproject.toml, own tests)
infra/                                  Terraform: modules/ + environments/{dev,staging,prod}
```

## Logging (see ADR §10.1, `backend/shared/Thor.Core/README.md`)
- Every service bootstraps logging via `Thor.Core.Logging.UseThorLogging()`; format,
  level, and sinks are configured per-service in `appsettings.json`, never hard-coded.
- Attach request-scoped context (e.g. `TenantId`) via `LogContext`
  (`HeaderLogEnrichmentMiddleware` or `ThorLogContext`), not method parameters —
  business logic stays a plain `ILogger<T>` consumer.
- Use the level that matches the event, log every caught exception, and always use
  structured message templates (`"... {Foo}"`), never string interpolation — a
  filtered-out level should cost nothing.

## Non-negotiable principles (see ADR §1, §5, §6)
- **Tenant isolation by default.** Every data path resolves tenant from a *trusted*
  source and **fails closed** if the tenant can't be verified.
- **Never trust spoofable input.** Subdomains, headers, API keys, file paths are
  *hints* only; authority comes from the tenant metadata store or a verified token.
- **Routing, not DB name, is the scaling primitive.** Tenant routing resolves to
  `{ clusterEndpoint, databaseName, dbUser, secretRef }`. Never hard-code DB names.
- **Workflow modules are independent and additive**, following one pattern:
  queue → Step Functions → compute → DLQ, idempotent, tenant-scoped.

## Working Principles
### 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

### 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

### 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

### 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

### 5. Modular & Layered

**Small, single-purpose modules. Import only what you need.**

- Organize each .NET service layer-first: `Controllers/` (HTTP only), `Services/`
  (business logic), `Repositories/` (data access), `Models/` (DTOs/schemas). Controllers
  stay thin and delegate downward; dependencies point one way
  (Controllers → Services → Repositories), never back.
- Keep types small and single-purpose. A consumer should reference one focused class to
  use one function — never pull in a bulky catch-all. No grab-bag `Utils`/`Helpers`
  god-classes.
- Services share no endpoint/feature code. Cross-service reuse happens only through
  granular, purpose-built shared libraries (`backend/shared/*`) referenced narrowly —
  the same "independent and additive" rule the Non-negotiable principles apply to
  workflow modules.
- Define each schema/contract once in its granular module and reference it; duplicated
  definitions are a smell.