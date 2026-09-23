data "aws_iam_policy_document" "ecs_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

# One execution role per service (never shared). Master DB auth is RDS IAM end-to-end, so the only
# `secrets` a task definition declares are the connector JWT keypair and the API-key pepper —
# granted below on the *bare* secret ARN, since the ":json-key::" form a valueFrom accepts matches
# nothing as an IAM resource.
resource "aws_iam_role" "execution" {
  for_each = local.active_services

  name                 = "${local.name_prefix[each.key]}-execution"
  assume_role_policy   = data.aws_iam_policy_document.ecs_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_iam_role_policy_attachment" "execution_managed" {
  for_each = local.active_services

  role       = aws_iam_role.execution[each.key].name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

# Task role — the app's own runtime permissions. X-Ray is the only baseline grant.
resource "aws_iam_role" "task" {
  for_each = local.active_services

  name                 = "${local.name_prefix[each.key]}-task"
  assume_role_policy   = data.aws_iam_policy_document.ecs_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_iam_role_policy_attachment" "task_xray" {
  for_each = local.active_services

  role       = aws_iam_role.task[each.key].name
  policy_arn = "arn:aws:iam::aws:policy/AWSXRayDaemonWriteAccess"
}

# rds-db:connect for the DB-accessing services (client->proxy IAM leg). Two dbusers: the Master
# DB read role (master_db_app_user) and the per-tenant roles (tenant_* covers _rw and _ro). Only
# granted to services listed in db_access_service_keys that are actually active.
#
# The ARN names the RDS Proxy (prx-...), not the Aurora cluster: these services connect to the
# proxy endpoint, and for that leg AWS scopes rds-db:connect by proxy resource ID. A cluster-scoped
# ARN authorizes nothing here. The proxy's own role covers the proxy->DB leg and is cluster-scoped.
data "aws_caller_identity" "current" {}

locals {
  db_access_services = {
    for k in var.db_access_service_keys : k => k
    if contains(keys(local.active_services), k) && var.rds_proxy_resource_id != ""
  }
  rds_db_arn_prefix = "arn:aws:rds-db:${data.aws_region.current.region}:${data.aws_caller_identity.current.account_id}:dbuser:${var.rds_proxy_resource_id}"
}

resource "aws_iam_role_policy" "task_rds_connect" {
  for_each = local.db_access_services

  name = "rds-db-connect"
  role = aws_iam_role.task[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = ["rds-db:connect"]
      Resource = [
        "${local.rds_db_arn_prefix}/${var.master_db_app_user}",
        "${local.rds_db_arn_prefix}/tenant_*",
      ]
    }]
  })
}

# Execution-role grant for every secret a service's `secrets` map pulls from. ECS resolves these
# at task start with the *execution* role, not the task role — a grant on the task role leaves the
# container stuck in PENDING with a ResourceInitializationError instead.
locals {
  services_with_secret_arns = {
    for k, arns in var.execution_secret_arns : k => arns
    if contains(keys(local.active_services), k)
  }
}

resource "aws_iam_role_policy" "execution_secrets" {
  for_each = local.services_with_secret_arns

  name = "secrets-read"
  role = aws_iam_role.execution[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["secretsmanager:GetSecretValue"]
      Resource = each.value
    }]
  })
}

# task-api presigns uploads with its own task-role credentials, so the signature is only as
# permissive as this grant — a presigned PUT the role couldn't perform itself is rejected at
# redemption time.
#
# for_each keys off active_services alone: the bucket ARN isn't known until apply, and a for_each
# whose membership depends on an unknown value fails at plan. Using it in the policy body is fine.
resource "aws_iam_role_policy" "task_uploads_s3" {
  for_each = toset(contains(keys(local.active_services), "task-api") ? ["task-api"] : [])

  name = "uploads-s3"
  role = aws_iam_role.task[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["s3:PutObject", "s3:GetObject", "s3:AbortMultipartUpload"]
      Resource = "${var.uploads_bucket_arn}/*"
    }]
  })
}

# thor-api stores each tenant's connector credentials as its own Secrets Manager secret, named
# "tenant/<tenantId>/<authenticationTypeId>" by AuthenticationSecretNaming. Create and Put are
# both needed: the writer creates the secret on first use and overwrites it thereafter.
resource "aws_iam_role_policy" "task_tenant_secrets" {
  for_each = toset(contains(keys(local.active_services), "thor-api") ? ["thor-api"] : [])

  name = "tenant-credential-secrets"
  role = aws_iam_role.task[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "secretsmanager:CreateSecret",
        "secretsmanager:GetSecretValue",
        "secretsmanager:PutSecretValue",
      ]
      Resource = "arn:aws:secretsmanager:${data.aws_region.current.region}:${data.aws_caller_identity.current.account_id}:secret:tenant/*"
    }]
  })
}
