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

locals {
  name_prefix = "thor-${var.environment}-db-bootstrap"
}

# Terraform zips a pre-published directory — it does not compile. The root.hcl before_hook runs
# dotnet publish into var.source_dir first (same pattern as modules/lambda).
# output_path uses dirname(), not "${var.source_dir}/..": CI's apply job restores the zip alone, never
# publish/, and a path routed through a directory that doesn't exist fails to open even when the file does.
data "archive_file" "bootstrap" {
  type        = "zip"
  source_dir  = var.source_dir
  output_path = "${dirname(var.source_dir)}/db-bootstrap-build.zip"
}

# --- security group (in-VPC: reaches the Aurora writer on 5432 + Secrets Manager endpoint on 443) ---
# name_prefix + create_before_destroy: name/description/vpc_id are all ForceNew, and a destroy-first
# replacement cannot complete once this SG is attached to a Lambda — the group stays in-use until the
# service reaps the ENI, which only happens after the vpc_config update that Terraform orders behind
# the delete. Creating the replacement first lets the function move off before the old group goes.
resource "aws_security_group" "bootstrap" {
  name_prefix = "${local.name_prefix}-sg-"
  description = "DB bootstrap Lambda - egress to VPC only (Aurora writer 5432, Secrets Manager endpoint 443)"
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

# The bootstrap connects direct to the Aurora writer for DDL (like tenant provisioning), so its SG
# needs ingress to Aurora on 5432. Rule lives here (not in the aurora module) to avoid a module
# dependency cycle — mirrors the provisioning module's provisioning_to_aurora rule.
resource "aws_vpc_security_group_ingress_rule" "bootstrap_to_aurora" {
  security_group_id            = var.aurora_security_group_id
  description                  = "PostgreSQL from the DB bootstrap Lambda"
  referenced_security_group_id = aws_security_group.bootstrap.id
  from_port                    = 5432
  to_port                      = 5432
  ip_protocol                  = "tcp"

  tags = merge(var.tags, { Name = "${local.name_prefix}-aurora-ingress" })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# --- IAM ---
data "aws_iam_policy_document" "assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "bootstrap" {
  name                 = "${local.name_prefix}-role"
  assume_role_policy   = data.aws_iam_policy_document.assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_cloudwatch_log_group" "bootstrap" {
  name              = "/aws/lambda/${local.name_prefix}"
  retention_in_days = 30
  tags              = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Logs to own log group only + read the AWS-managed master secret + VPC ENI management (the ec2
# actions can't be resource-scoped). No rds-db:connect: the bootstrap authenticates with the
# master password from the secret, not IAM.
data "aws_iam_policy_document" "permissions" {
  statement {
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
    resources = ["${aws_cloudwatch_log_group.bootstrap.arn}:*"]
  }

  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [var.master_user_secret_arn]
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
  name   = "db-bootstrap-permissions"
  role   = aws_iam_role.bootstrap.id
  policy = data.aws_iam_policy_document.permissions.json
}

resource "aws_lambda_function" "bootstrap" {
  function_name = local.name_prefix
  role          = aws_iam_role.bootstrap.arn
  runtime       = var.runtime
  handler       = "Thor.DbBootstrap.Function::Thor.DbBootstrap.Function.Function::FunctionHandler"
  timeout       = var.timeout
  memory_size   = var.memory_size

  filename         = data.archive_file.bootstrap.output_path
  source_code_hash = data.archive_file.bootstrap.output_base64sha256

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [aws_security_group.bootstrap.id]
  }

  environment {
    variables = {
      THOR_BOOTSTRAP_SECRET_ARN      = var.master_user_secret_arn
      THOR_BOOTSTRAP_HOST            = var.aurora_writer_endpoint
      THOR_BOOTSTRAP_DATABASE        = var.aurora_database_name
      THOR_BOOTSTRAP_PORT            = "5432"
      THOR_BOOTSTRAP_SKIP_MIGRATIONS = var.skip_migrations ? "true" : "false"
    }
  }

  depends_on = [aws_cloudwatch_log_group.bootstrap, aws_iam_role_policy.permissions]

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Runs the bootstrap at apply. Re-invokes whenever the function code changes (source_code_hash
# covers the embedded db-roles.sql), so a role/grant change redeploys and re-applies automatically.
# The script is idempotent, so re-invocation is a safe no-op when nothing changed. A non-zero
# handler result fails the apply — surfacing an unreachable DB or unreadable secret loudly.
resource "aws_lambda_invocation" "bootstrap" {
  function_name = aws_lambda_function.bootstrap.function_name
  input         = jsonencode({})

  triggers = {
    source_code_hash = aws_lambda_function.bootstrap.source_code_hash
  }
}
