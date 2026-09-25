terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
    archive = {
      source  = "hashicorp/archive"
      version = "~> 2.0"
    }
  }
}

data "aws_region" "current" {}
data "aws_caller_identity" "current" {}

locals {
  name_prefix = "thor-${var.environment}-master-db-seed"

  # Scoped to the proxy (prx-...), not the cluster: the seed Lambda connects to the proxy endpoint,
  # and rds-db:connect for that leg is authorized by proxy resource ID.
  rds_db_arn = "arn:aws:rds-db:${data.aws_region.current.region}:${data.aws_caller_identity.current.account_id}:dbuser:${var.rds_proxy_resource_id}/${var.master_seed_db_user}"
}

# source_dir/output_path are absolute (via get_repo_root()) since Terragrunt only copies infra/src,
# not backend/. Requires dotnet publish into source_dir first — Terraform zips, it doesn't compile.
# output_path uses dirname(), not "${var.source_dir}/..": CI's apply job restores the zip alone, never
# publish/, and a path routed through a directory that doesn't exist fails to open even when the file does.
data "archive_file" "master_db_seed" {
  type        = "zip"
  source_dir  = var.source_dir
  output_path = "${dirname(var.source_dir)}/master-db-seed-build.zip"
}

# Runs in-VPC so it can reach the Master DB through the RDS Proxy over TCP. Writes deployment seed
# data with an RDS IAM token as the least-privilege thor_master_seed role — no password, no RDS
# Data API (the DB is never reachable from outside the VPC).
#
# name_prefix + create_before_destroy: name/description/vpc_id are all ForceNew, and a destroy-first
# replacement cannot complete once this SG is attached to a Lambda — the group stays in-use until the
# service reaps the ENI, which only happens after the vpc_config update that Terraform orders behind
# the delete. Creating the replacement first lets the function move off before the old group goes.
resource "aws_security_group" "master_db_seed" {
  name_prefix = "${local.name_prefix}-sg-"
  description = "Master DB seed Lambda - egress to VPC only (reach the RDS Proxy on 5432)"
  vpc_id      = var.vpc_id

  egress {
    description = "Within VPC only"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = [var.vpc_cidr]
  }

  tags = merge(var.tags, { Name = "${local.name_prefix}-sg" })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    create_before_destroy = true
    ignore_changes        = [tags, tags_all]
  }
}

# The seed Lambda connects through the RDS Proxy, so it needs ingress to the proxy on 5432. Rule
# lives here (not in the rds_proxy module) to avoid a module dependency cycle — same pattern as
# the authorizer module's ingress onto the proxy SG.
resource "aws_vpc_security_group_ingress_rule" "master_db_seed_to_proxy" {
  security_group_id            = var.rds_proxy_security_group_id
  description                  = "PostgreSQL from the Master DB seed Lambda"
  referenced_security_group_id = aws_security_group.master_db_seed.id
  from_port                    = 5432
  to_port                      = 5432
  ip_protocol                  = "tcp"

  tags = merge(var.tags, { Name = "${local.name_prefix}-proxy-ingress" })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

data "aws_iam_policy_document" "assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "master_db_seed" {
  name                 = "${local.name_prefix}-role"
  assume_role_policy   = data.aws_iam_policy_document.assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_cloudwatch_log_group" "master_db_seed" {
  name              = "/aws/lambda/${local.name_prefix}"
  retention_in_days = 30
  tags              = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Logs to own log group only + rds-db:connect scoped to thor_master_seed (mints an IAM token
# through the proxy) + VPC ENI management (the ec2 actions can't be resource-scoped). No
# Secrets Manager permission — no secret is ever read.
data "aws_iam_policy_document" "permissions" {
  statement {
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
    resources = ["${aws_cloudwatch_log_group.master_db_seed.arn}:*"]
  }

  statement {
    actions   = ["rds-db:connect"]
    resources = [local.rds_db_arn]
  }

  statement {
    actions = [
      "ec2:CreateNetworkInterface",
      "ec2:DescribeNetworkInterfaces",
      "ec2:DeleteNetworkInterface",
      "ec2:AssignPrivateIpAddresses",
      "ec2:UnassignPrivateIpAddresses",
    ]
    resources = ["*"]
  }
}

resource "aws_iam_role_policy" "permissions" {
  name   = "master-db-seed-permissions"
  role   = aws_iam_role.master_db_seed.id
  policy = data.aws_iam_policy_document.permissions.json
}

resource "aws_lambda_function" "master_db_seed" {
  function_name = local.name_prefix
  role          = aws_iam_role.master_db_seed.arn
  runtime       = var.runtime
  # Matches Thor.MasterDbSeed.Function's assembly/namespace/class (backend/functions/Thor.MasterDbSeed/src).
  handler     = "Thor.MasterDbSeed.Function::Thor.MasterDbSeed.Function.Function::FunctionHandler"
  timeout     = var.timeout
  memory_size = var.memory_size

  filename         = data.archive_file.master_db_seed.output_path
  source_code_hash = data.archive_file.master_db_seed.output_base64sha256

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [aws_security_group.master_db_seed.id]
  }

  environment {
    variables = {
      THOR_MASTERDB_HOST     = var.rds_proxy_endpoint
      THOR_MASTERDB_DATABASE = var.aurora_database_name
      THOR_MASTERDB_USER     = var.master_seed_db_user
      THOR_MASTERDB_REGION   = data.aws_region.current.region
      THOR_MASTERDB_PORT     = "5432"
    }
  }

  depends_on = [aws_cloudwatch_log_group.master_db_seed, aws_iam_role_policy.permissions]

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Runs the seed Lambda at apply. Re-invokes whenever the function code changes (source_code_hash
# covers the embedded DeploymentSeed scripts too, so adding/editing a script redeploys and
# re-applies automatically). Scripts are idempotent, so re-invocation is a safe no-op when nothing
# changed. A non-zero handler result fails the apply — surfacing an unreachable DB or bad script
# loudly.
resource "aws_lambda_invocation" "master_db_seed" {
  function_name = aws_lambda_function.master_db_seed.function_name
  input         = jsonencode({})

  triggers = {
    source_code_hash = aws_lambda_function.master_db_seed.source_code_hash
  }
}
