# Thor Infra

Terraform (via Terragrunt) for Thor's AWS infrastructure — one state per environment.

## Layout
```
infra/
  root.hcl                  Terragrunt root: per-env backend (JFrog remote state) + AWS provider generation
  envs/{dev,qa,prod}/        Per-environment terragrunt.hcl — all inputs to the root module
  src/                       Root Terraform module composing all infra components
    main.tf                  Wires modules together
    modules/
      network/                VPC, subnets, AZs, VPC interface endpoints
      ecs/                     ECS cluster, Service Connect namespace, ECR repos, services, NLBs, SGs, IAM
      aurora/                  Aurora PostgreSQL Serverless v2 (task-api's database)
      api_gateway/             API Gateway fronting thor-api's NLB
      frontend/                S3 + CloudFront (OAC) static hosting for the SPA
      secrets/                 Secrets Manager secrets whose values are seeded out-of-band
      uploads/                 Private S3 bucket task-api presigns tenant uploads against
  bootstrap/                 Setup that Terraform creates a placeholder for but cannot fill itself
```

## Environments
- **dev / qa** — share one AWS account, isolated per-environment state/resources.
- **prod** — separate, isolated AWS account.

Each environment sets its own account/region (`root.hcl`) and sizing/feature flags (`envs/<env>/terragrunt.hcl`), e.g. `enable_compute`, `enable_frontend`, ECS service specs, and Aurora capacity.

## Notes
- The ECS cluster, Service Connect namespace, ECR repos, and Aurora database are always created; `enable_compute` gates whether ECS services/NLB listeners/IAM roles actually deploy (so infra can exist before an image is pushed).
- `api_gateway` depends on `enable_compute` (needs thor-api's NLB listener).
- RDS Proxy and Neptune are not yet implemented.

## Out-of-band setup

Terraform creates these secrets but never holds their values. An environment is not functional
until each has been seeded — the services fail closed at startup without them.

| What | How | Docs |
|------|-----|------|
| Master DB schema + platform roles | Automatic, at `apply` (in-VPC Lambda) | [bootstrap/README.md](bootstrap/README.md) |
| Connector JWT keypair + API-key pepper | `scripts/seed-connector-secrets.sh <env>` | [bootstrap/connector-secrets.md](bootstrap/connector-secrets.md) |
| Authorizer PBKDF2 salt | Set `thor-<env>-secret-authorizer-salt` by hand (base64 bytes) | — |
