# Thor.TenantProvisioning

Tenant onboarding as an AWS Step Functions workflow (ADR §11 — the "lift" of the manual
onboarding runbook). Six Lambda handlers, one per step, orchestrated by the state machine
in [`statemachine/tenant-provisioning.asl.json`](statemachine/tenant-provisioning.asl.json).

The workflow is triggered on-demand (e.g. a GitHub Actions `workflow_dispatch` calling
`aws stepfunctions start-execution`) with the tenant's onboarding details as input.

## Steps

| # | State | Handler class | Does |
|---|-------|---------------|------|
| 1 | `SeedTenantMetadata` | `SeedTenantMetadataFunction` | Insert the `tenant` row (status `Provisioning`); returns `TenantId`. |
| 2 | `CreateTenantDatabase` | `CreateTenantDatabaseFunction` | Create the tenant DB + `_rw`/`_ro` `rds_iam` roles on the cluster (passwordless). |
| 3 | `ProvisionCognito` | `ProvisionCognitoFunction` | Ensure the per-tenant Cognito user pool + app client + `admins` group. |
| 4 | `CreateAdminUser` | `CreateAdminUserFunction` | `AdminCreateUser` for the local admin + add to group (Cognito sends the invite). |
| 5 | `ConfigureSubdomain` | `ConfigureSubdomainFunction` | UPSERT the Route53 record `{subdomain}.{baseDomain}`. |
| 6 | `FinalizeRouting` | `FinalizeRoutingFunction` | Upsert `tenant_routing` (the `_rw`/`_ro` **db-user names**) and flip the tenant to `Active`. |

Every step is **idempotent** so Step Functions retries / DLQ redrive replay safely
(ADR §16). The tenant is only marked `Active` in the final step (fail-closed, ADR §1).

## Auth model (see the plan for rationale)

RDS IAM database authentication **everywhere — no passwords, no Secrets Manager DB secrets.**

- **Control-plane (DDL + Master-DB writes):** steps connect as `thor_provisioner` using a
  short-lived IAM token from the Lambda's execution role, direct to the Aurora writer.
- **Data-plane (per-tenant runtime):** each tenant gets `_rw`/`_ro` roles that are
  `WITH LOGIN` + `GRANT rds_iam` (no password). The runtime mints an IAM token for these role
  names and connects through the IAM RDS Proxy. `tenant_routing` stores the **db-user names**,
  not secret ARNs. DB-enforced isolation per tenant is unchanged.

## Layout

- `src/Thor.TenantProvisioning.Core` — step logic + abstractions (`ICognitoProvisioner`,
  `ISubdomainProvisioner`, `ITenantDatabaseProvisioner`), no AWS SDK. References
  `Thor.DataLayer` for the Master DB.
- `src/Thor.TenantProvisioning.Function` — thin Lambda handlers, DI composition root, and
  the AWS SDK implementations (Cognito, Route53, Postgres/RDS-IAM).
- `test/Thor.TenantProvisioning.Test` — xUnit step tests (fakes for repos/provisioners).

## Handler strings (per Lambda)

```
Thor.TenantProvisioning.Function::Thor.TenantProvisioning.Function.Handlers.SeedTenantMetadataFunction::Handle
Thor.TenantProvisioning.Function::Thor.TenantProvisioning.Function.Handlers.CreateTenantDatabaseFunction::Handle
Thor.TenantProvisioning.Function::Thor.TenantProvisioning.Function.Handlers.ProvisionCognitoFunction::Handle
Thor.TenantProvisioning.Function::Thor.TenantProvisioning.Function.Handlers.CreateAdminUserFunction::Handle
Thor.TenantProvisioning.Function::Thor.TenantProvisioning.Function.Handlers.ConfigureSubdomainFunction::Handle
Thor.TenantProvisioning.Function::Thor.TenantProvisioning.Function.Handlers.FinalizeRoutingFunction::Handle
```

## Environment variables (Lambda config)

| Var | Used by | Notes |
|-----|---------|-------|
| `THOR_MASTERDB_HOST` / `_DATABASE` / `_USER` / `_PORT` | steps 1, 6 | Master DB connection (IAM — no password). Host is the Aurora writer. |
| `THOR_MASTERDB_SSL` | steps 1, 6 | Optional; `false` disables SSL. Defaults on. |
| `THOR_PROVISIONING_CLUSTER_WRITER_ENDPOINT` | step 2 | Aurora **writer** endpoint for DDL. |
| `THOR_PROVISIONING_REGION` | steps 1, 2, 6 | Region (RDS IAM token minting + routing row). |
| `THOR_PROVISIONING_ROUTING_ENDPOINT` | step 6 | Endpoint written to routing — the IAM **RDS Proxy** endpoint (ADR §6.3). |
| `THOR_PROVISIONING_DB_USER` | step 2 | Provisioning role; default `thor_provisioner`. |
| `THOR_PROVISIONING_ADMIN_DB` | step 2 | Maintenance DB for `CREATE DATABASE`; default `postgres`. |
| `THOR_PROVISIONING_HOSTED_ZONE_ID` / `_BASE_DOMAIN` / `_DNS_TARGET` | step 5 | Route53 record. |

AWS credentials/region come from the Lambda execution role (ambient credential chain).
**No passwords or secret values** flow through env, workflow input, state, or logs.

## Example StartExecution input

```json
{
  "DisplayName": "Acme Inc",
  "Subdomain": "acme",
  "AdminEmail": "admin@acme.example.com",
  "TierId": 1,
  "IsolationTypeId": 1
}
```

The tenant database, db-user names, and DB routing fields are **produced** by the workflow
(cluster endpoint + region come from Lambda config, not input).

## Infra

Deployed by `infra/src/modules/tenant_provisioning` (state machine + the six Lambdas + IAM +
VPC placement for the DB steps), gated by `enable_tenant_provisioning`. The `thor_provisioner`
role is bootstrapped once via [`infra/bootstrap`](../../../infra/bootstrap/README.md). The
GitHub trigger is [`.github/workflows/provision-tenant.yml`](../../../.github/workflows/provision-tenant.yml).

> Scope note: this workflow creates the tenant's Aurora database + roles, Cognito, and
> subdomain. Creating a **Neptune partition** per tenant (also in ADR §11) is a later increment.
