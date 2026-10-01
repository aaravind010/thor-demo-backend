# Gotchas

Non-obvious traps, 2-3 lines each. Add one when a bug took real digging to diagnose.

- **SigV4 query strings:** a `403 "signature we calculated does not match"` from Neptune on a request with a query string means the query went into `ResourcePath`.
  `AWS4Signer` signs only `request.Parameters` as the query. Keep the path and query separate (`NeptuneSigV4Signer`), and have tests compare the signature itself.
- **IAM permissions boundary:** every `aws_iam_role` needs `permissions_boundary`, or CI's `iam:CreateRole` gets `AccessDenied`.
  The boundary also caps what the role can do at runtime, and it's scoped by resource-name prefix, so renaming resources silently strips permissions.
- **Deploy role `thor-deploy-<env>`** is hand-created, not managed by Terraform, and its live policy differs from the setup guide in both directions.
  Read the live policy before "fixing" CI permissions.
- **`aws iam simulate-principal-policy`** returns `implicitDeny` for conditional grants unless you pass the condition keys via `--context-entries`.
  `--resource-arns "*"` is a literal ARN, not a wildcard.
- **Ingestion tests need Docker:** `Thor.Workflows.Ingestion.Tests` uses Testcontainers.
  With Docker down, about 89 tests fail. That's environmental, so treat those tests as unverified rather than as a regression.
- **Local infra tooling:** local Terraform/Terragrunt are newer than CI's pinned versions (`.github/actions/terragrunt-setup`), so a local pass isn't sufficient.
  `terragrunt plan` needs `SKIP_LAMBDA_PUBLISH=true` or a prior `dotnet publish`.
