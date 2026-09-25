# Infra Pipeline Setup Guide

This document explains what needs to exist in AWS and GitHub before the
`infra.yml` GitHub Actions workflow can run — the pipeline that plans and
applies Terraform/Terragrunt changes under `infra/` for `dev`, `qa`, and
`prod`.

## Overview

`infra.yml` needs four things in place before it can do anything:

1. Each environment's real AWS account ID, filled into `infra/root.hcl`.
2. A GitHub OIDC identity provider registered in AWS.
3. Three IAM roles — `deploy-dev`, `deploy-qa`, `deploy-prod` — **shared
   with the backend deploy pipeline** (`_deploy-core.yml`), not infra-only
   roles. One role per environment covers both purposes.
4. Four repo-level variables (`DEV_AWS_ROLE_ARN`/`QA_AWS_ROLE_ARN`/
   `PROD_AWS_ROLE_ARN`/`AWS_REGION`) plus the `dev`/`qa`/`prod` GitHub
   Environments (already needed by backend deploy) — `qa`/`prod`
   configured with required reviewers, `dev` left unprotected.

**File location:** `.github/workflows/infra.yml`

## How this differs from a per-job-environment setup

Only two of `infra.yml`'s six jobs (`apply-terragrunt`, `seed-ecr-placeholder`)
set a GitHub Environment at all — `init-terragrunt`/`validate-terragrunt`/
`plan-terragrunt` never do, for any branch, since they're read-only and
there's nothing to gate. `apply-terragrunt`/`seed-ecr-placeholder` run
under the environment `context` resolved (`dev`/`qa`/`prod`) — the same
three Environments the backend deploy pipeline already uses. `dev` has no
required-reviewer rule, so it still applies fully automatically; only
`qa`/`prod` actually pause for approval. AWS role resolution is
**repo-level** for every job (`vars.DEV_AWS_ROLE_ARN` etc., picked with a
chained `&&`/`||` expression on the resolved environment), not
GitHub-Environment-scoped like a typical per-environment setup — that
part stays true regardless of which Environment (if any) a job runs
under. This means there's no `dev-infra-plan`/`dev-infra-apply`/
`qa-infra-plan`/etc. — just the three roles and the three shared
Environments.

## Step 1: Configure AWS account IDs (`infra/root.hcl`)

Before touching AWS or GitHub, `infra/root.hcl`'s `account_map` needs each
environment's real AWS account ID:

```hcl
account_map = {
  dev  = { account_name = "dev",  account_id = "<DEV_ACCOUNT_ID>",  aws_region = "us-west-2" }
  qa   = { account_name = "qa",   account_id = "<QA_ACCOUNT_ID>",   aws_region = "us-west-2" }
  prod = { account_name = "prod", account_id = "<PROD_ACCOUNT_ID>", aws_region = "us-west-2" }
}
```

This isn't cosmetic — `account_id` is what builds the S3 state bucket's
name (`thor-terraform-state-<account_id>`), so a blank or wrong value
here means `init-terragrunt` can't find or auto-create the right bucket.

## Step 2: Create the OIDC identity provider (AWS Console)

Once per AWS account:

1. **IAM → Identity providers → Add provider**
2. Provider type: **OpenID Connect**
3. Provider URL: `https://token.actions.githubusercontent.com`
4. Audience: `sts.amazonaws.com`
5. **Add provider**

## Step 3: Create the three deploy roles (AWS Console)

Repeat this for each of `dev`, `qa`, `prod` — three separate roles,
`deploy-dev`/`deploy-qa`/`deploy-prod`.

1. **IAM → Roles → Create role**
2. Trusted entity type: **Web identity**
3. Identity provider: the one from Step 2
4. Audience: `sts.amazonaws.com`
5. Skip the console's repo/branch fields (only support one combo — each of
   these roles needs several trusted at once). Create the role, then edit
   its **Trust relationships** tab and replace the policy with the one
   below for that role (fill in `<ACCOUNT_ID>` and `<GITHUB_ORG>@
   <GITHUB_ORG_ID>/<REPO>@<REPO_ID>`, same subject-claim format used
   elsewhere in this repo's IAM setup):

   **`deploy-dev`:**
   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       {
         "Effect": "Allow",
         "Principal": { "Federated": "arn:aws:iam::<ACCOUNT_ID>:oidc-provider/token.actions.githubusercontent.com" },
         "Action": "sts:AssumeRoleWithWebIdentity",
         "Condition": {
           "StringEquals": { "token.actions.githubusercontent.com:aud": "sts.amazonaws.com" },
           "StringLike": {
             "token.actions.githubusercontent.com:sub": [
               "repo:<GITHUB_ORG>@<GITHUB_ORG_ID>/<REPO>@<REPO_ID>:environment:dev",
               "repo:<GITHUB_ORG>@<GITHUB_ORG_ID>/<REPO>@<REPO_ID>:ref:refs/heads/dev",
               "repo:<GITHUB_ORG>@<GITHUB_ORG_ID>/<REPO>@<REPO_ID>:pull_request"
             ]
           }
         }
       }
     ]
   }
   ```

   **`deploy-qa`:**
   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       {
         "Effect": "Allow",
         "Principal": { "Federated": "arn:aws:iam::<ACCOUNT_ID>:oidc-provider/token.actions.githubusercontent.com" },
         "Action": "sts:AssumeRoleWithWebIdentity",
         "Condition": {
           "StringEquals": { "token.actions.githubusercontent.com:aud": "sts.amazonaws.com" },
           "StringLike": {
             "token.actions.githubusercontent.com:sub": [
               "repo:<GITHUB_ORG>@<GITHUB_ORG_ID>/<REPO>@<REPO_ID>:environment:qa-candidate",
               "repo:<GITHUB_ORG>@<GITHUB_ORG_ID>/<REPO>@<REPO_ID>:environment:qa",
               "repo:<GITHUB_ORG>@<GITHUB_ORG_ID>/<REPO>@<REPO_ID>:ref:refs/heads/qa",
               "repo:<GITHUB_ORG>@<GITHUB_ORG_ID>/<REPO>@<REPO_ID>:pull_request"
             ]
           }
         }
       }
     ]
   }
   ```

   **`deploy-prod`** (branch is `main`, not `prod` — that's the actual branch name this environment deploys from):
   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       {
         "Effect": "Allow",
         "Principal": { "Federated": "arn:aws:iam::<ACCOUNT_ID>:oidc-provider/token.actions.githubusercontent.com" },
         "Action": "sts:AssumeRoleWithWebIdentity",
         "Condition": {
           "StringEquals": { "token.actions.githubusercontent.com:aud": "sts.amazonaws.com" },
           "StringLike": {
             "token.actions.githubusercontent.com:sub": [
               "repo:<GITHUB_ORG>@<GITHUB_ORG_ID>/<REPO>@<REPO_ID>:environment:prod",
               "repo:<GITHUB_ORG>@<GITHUB_ORG_ID>/<REPO>@<REPO_ID>:ref:refs/heads/main",
               "repo:<GITHUB_ORG>@<GITHUB_ORG_ID>/<REPO>@<REPO_ID>:pull_request"
             ]
           }
         }
       }
     ]
   }
   ```

   The `ref:refs/heads/<branch>` entries exist because `infra.yml`'s
   `init`/`validate`/`plan` never set a GitHub Environment, for any
   branch — a push presents a `ref:`-based subject there instead of an
   `environment:`-based one. (`apply-terragrunt`/`seed-ecr-placeholder`
   run under a real Environment for all three environments now, so those
   two jobs authenticate via the `environment:<name>` subjects instead.)

   **Real GitHub OIDC limitation worth knowing**: the `pull_request`
   subject is identical regardless of which branch the PR targets —
   there's no way to scope "only PRs into dev" via the `sub` claim alone.
   All three roles need to trust it. Low risk in practice, since
   PR-triggered jobs are read-only (nothing applies from a PR).

6. Attach an inline permissions policy (**Add permissions → Create inline
   policy → JSON**) — the same shape for all three roles, just with
   `<environment>` swapped for `dev`/`qa`/`prod`. Resources are scoped to
   the `thor-<environment>-*` naming convention already used throughout
   the Terraform modules **wherever that's actually possible** — some
   services don't support it (see the notes after the policy) and stay
   `Resource: "*"` on purpose, not by oversight:

   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       { "Sid": "Ec2Networking", "Effect": "Allow", "Action": ["ec2:*"], "Resource": "*" },
       {
         "Sid": "EcrPushPull",
         "Effect": "Allow",
         "Action": ["ecr:*"],
         "Resource": "arn:aws:ecr:*:*:repository/*-<environment>"
       },
       {
         "Sid": "EcsDeploy",
         "Effect": "Allow",
         "Action": ["ecs:*"],
         "Resource": [
           "arn:aws:ecs:*:*:cluster/thor-<environment>",
           "arn:aws:ecs:*:*:service/thor-<environment>/*",
           "arn:aws:ecs:*:*:task-definition/thor-<environment>-*:*",
           "arn:aws:ecs:*:*:task/thor-<environment>/*"
         ]
       },
       {
         "Sid": "LoadBalancing",
         "Effect": "Allow",
         "Action": ["elasticloadbalancing:*"],
         "Resource": [
           "arn:aws:elasticloadbalancing:*:*:loadbalancer/net/thor-<environment>-*/*",
           "arn:aws:elasticloadbalancing:*:*:targetgroup/thor-<environment>-*/*",
           "arn:aws:elasticloadbalancing:*:*:listener/net/thor-<environment>-*/*/*"
         ]
       },
       { "Sid": "ApiGateway", "Effect": "Allow", "Action": ["apigateway:*"], "Resource": "*" },
       { "Sid": "CloudFront", "Effect": "Allow", "Action": ["cloudfront:*"], "Resource": "*" },
       { "Sid": "ServiceDiscovery", "Effect": "Allow", "Action": ["servicediscovery:*"], "Resource": "*" },
       {
         "Sid": "S3FrontendBucket",
         "Effect": "Allow",
         "Action": ["s3:*"],
         "Resource": ["arn:aws:s3:::thor-frontend-<environment>-*", "arn:aws:s3:::thor-frontend-<environment>-*/*"]
       },
       {
         "Sid": "S3TerraformState",
         "Effect": "Allow",
         "Action": ["s3:*"],
         "Resource": ["arn:aws:s3:::thor-terraform-state-*", "arn:aws:s3:::thor-terraform-state-*/*"]
       },
       {
         "Sid": "Rds",
         "Effect": "Allow",
         "Action": ["rds:*"],
         "Resource": [
           "arn:aws:rds:*:*:cluster:thor-<environment>-aurora",
           "arn:aws:rds:*:*:db:thor-<environment>-aurora-*",
           "arn:aws:rds:*:*:subgrp:thor-<environment>-aurora"
         ]
       },
       { "Sid": "SecretsManager", "Effect": "Allow", "Action": ["secretsmanager:*"], "Resource": "*" },
       {
         "Sid": "Lambda",
         "Effect": "Allow",
         "Action": ["lambda:*"],
         "Resource": "arn:aws:lambda:*:*:function:thor-<environment>-*"
       },
       {
         "Sid": "Logs",
         "Effect": "Allow",
         "Action": ["logs:*"],
         "Resource": [
           "arn:aws:logs:*:*:log-group:/ecs/<environment>/*",
           "arn:aws:logs:*:*:log-group:/aws/lambda/thor-<environment>-*"
         ]
       },
       {
         "Sid": "ManageThorIamRoles",
         "Effect": "Allow",
         "Action": [
           "iam:CreateRole", "iam:DeleteRole", "iam:GetRole",
           "iam:PutRolePolicy", "iam:DeleteRolePolicy", "iam:GetRolePolicy",
           "iam:AttachRolePolicy", "iam:DetachRolePolicy",
           "iam:TagRole", "iam:UntagRole",
           "iam:ListRolePolicies", "iam:ListAttachedRolePolicies"
         ],
         "Resource": "arn:aws:iam::*:role/thor-<environment>-*"
       },
       { "Sid": "PassExecutionAndTaskRoles", "Effect": "Allow", "Action": ["iam:PassRole"], "Resource": "arn:aws:iam::*:role/thor-<environment>-*" }
     ]
   }
   ```

   **Scoped to `thor-<environment>-*`** (or a close variant): ECR, ECS,
   load balancing, S3 (both the frontend bucket and, separately, the
   Terraform state bucket — which doesn't carry `<environment>` in its
   name at all, since it's per-account not per-environment), RDS,
   Lambda, CloudWatch Logs, and IAM role management/`PassRole`.

   **Left as `Resource: "*"` on purpose, not by oversight:**
   - **EC2** — VPC/subnet/security-group IDs (`vpc-xxxxx`) are AWS-random,
     not name-derived. Scoping by name would mean scoping by the `Name`
     *tag* instead of the ARN, and many EC2 create actions don't support
     resource-level permissions or tag-on-create conditions consistently
     — getting this wrong is the likeliest way to break `terraform apply`
     outright (e.g. "create a new VPC" evaluated before the resource, and
     its tag, exist).
   - **API Gateway**, **CloudFront**, **Service Discovery** — all use
     AWS-random IDs in their ARNs (REST API ID, distribution ID,
     namespace/service ID), never the `thor-<environment>-*` name itself.
   - **Secrets Manager** — the Aurora master secret's ID is auto-generated
     by AWS (`rds!cluster-<random-uuid>`) when using
     `manage_master_user_password`, never derived from the naming
     convention at all.

   **Not fully verified — treat as best-effort, not guaranteed-exact**:
   the ARN formats above follow AWS's documented patterns, but a couple
   (the ELB listener ARN's segment count in particular) haven't been
   confirmed against a real apply in this account. If something gets
   denied unexpectedly, check the exact ARN in the CloudTrail/IAM error
   message against the pattern above before assuming the permission is
   simply missing.

7. Name the role (`deploy-dev`/`deploy-qa`/`deploy-prod`) and copy its
   **ARN** — needed in Step 4.

8. **Tenant migrations** (`.github/workflows/tenant-migrations.yml`,
   `migrations/tenant/README.md`). `scripts/create-deploy-role.sh` applies
   all of this; if you manage the roles in the console, add it by hand.

   Trust: add one subject per role:
   `repo:<GITHUB_ORG>@<GITHUB_ORG_ID>/<REPO>@<REPO_ID>:environment:<environment>-db-migration`
   (the `approve` job). The `ref:refs/heads/<branch>` subject above already
   covers the other jobs, which run without an Environment.

   Permissions: add these statements to the inline policy:
   ```json
   {
     "Sid": "TenantMigrationBucket",
     "Effect": "Allow",
     "Action": ["s3:*"],
     "Resource": ["arn:aws:s3:::thor-<environment>-tenant-migration-*", "arn:aws:s3:::thor-<environment>-tenant-migration-*/*"]
   },
   {
     "Sid": "TenantMigrationExecutions",
     "Effect": "Allow",
     "Action": ["states:StartExecution", "states:DescribeExecution", "states:StopExecution"],
     "Resource": [
       "arn:aws:states:*:*:stateMachine:thor-<environment>-tenant-migration",
       "arn:aws:states:*:*:execution:thor-<environment>-tenant-migration:*"
     ]
   },
   { "Sid": "TenantMigrationApproval", "Effect": "Allow", "Action": ["states:SendTaskSuccess", "states:SendTaskFailure"], "Resource": "*" }
   ```
   `SendTaskSuccess`/`SendTaskFailure` can't be resource-scoped (they act on
   a task token, not an ARN). Terraform's `module.tenant_migration` additionally creates, under
   this role: the ECR repo `thor-<environment>-tenant-migration-runner`, the
   ECS cluster and task-definition family `thor-<environment>-tenant-migration`,
   a state machine and CloudWatch alarm of the same name, and log groups
   `/ecs/<environment>/thor-<environment>-tenant-migration` and
   `/aws/states/thor-<environment>-tenant-migration` — make sure the ECR,
   ECS, Step Functions, CloudWatch and Logs statements cover those names.

## Step 4: Set the repo-level variables

**Repo → Settings → Secrets and variables → Actions → Variables** (repo
level, not environment level):

- `DEV_AWS_ROLE_ARN` = `deploy-dev`'s ARN
- `QA_AWS_ROLE_ARN` = `deploy-qa`'s ARN
- `PROD_AWS_ROLE_ARN` = `deploy-prod`'s ARN
- `AWS_REGION` (optional — defaults to `us-west-2` if unset)

## Step 5: Configure the `dev`/`qa`/`prod` GitHub Environments

These are the **same** Environments the backend deploy pipeline already
needs — no infra-specific Environments to create.

1. **Repo → Settings → Environments**
2. Create all three (`dev`, `qa`, `prod`) if they don't already exist.
3. `qa` and `prod`: add required reviewers. `dev`: leave unprotected —
   `apply-terragrunt`/`seed-ecr-placeholder` still run fully
   automatically there, it just runs under a real Environment now
   instead of none at all.
4. No `AWS_ROLE_ARN` variable needed on any of the three Environments for
   infra's sake — `infra.yml` reads the repo-level variables from Step 4
   regardless of which Environment a job is running under.

## Known temporary state: ECS service `load_balancer` lifecycle

`infra/src/modules/ecs/services.tf`'s `aws_ecs_service.thor-svc` has a
`lifecycle.ignore_changes` block:

```hcl
lifecycle {
  ignore_changes = [task_definition, desired_count]
}
```

`task_definition` and `desired_count` are ignored permanently — CI/CD
deploys new images and adjusts scaling directly against AWS (not through
Terraform), so Terraform must not fight those changes back on the next
`apply`.

`load_balancer` is **deliberately left out** of that list right now, and
that's a temporary state, not the intended end state. Blue/green ECS
deployments swap which target group is "live" outside of Terraform,
during each deploy — normally `load_balancer` should be ignored too, for
the same reason as `task_definition`/`desired_count`. But creating the
`advanced_configuration` block (the alternate target group, listener
rule, and swap role a `ROLLING` → `BLUE_GREEN` `deployment_strategy`
change needs) requires Terraform to actively manage `load_balancer` for
one apply — it would never get created if `load_balancer` were already
ignored.

**Follow-up required:** once that one BLUE_GREEN-enabling apply has
actually succeeded against real AWS (dev/qa/prod), add `load_balancer`
back to the ignore list:

```hcl
lifecycle {
  ignore_changes = [task_definition, desired_count, load_balancer]
}
```

Re-adding it too early — before that apply has run — would silently
prevent the `advanced_configuration` block from ever being created.
Confirm the apply status before making this change.

## Verifying it works

Open a PR that touches something under `infra/**` targeting `dev`. You
should see `init-terragrunt` → `validate-terragrunt` (includes `hcl fmt`
+ `tflint`) → `plan-terragrunt` run and post a plan as a PR comment, with
no AWS credential errors. Merging (or pushing directly to `dev`) should
then run `apply-terragrunt` automatically — for `qa`/`prod`, that job
pauses for a required reviewer's approval before applying.
