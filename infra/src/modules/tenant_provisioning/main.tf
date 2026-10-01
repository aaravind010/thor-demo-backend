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
  name_prefix = "thor-${var.environment}-tenant-provisioning"

  handler_ns = "Thor.TenantProvisioning.Function::Thor.TenantProvisioning.Function.Handlers"

  # One published artifact, seven functions — each a distinct handler in the same assembly.
  functions = {
    seed-tenant-metadata   = { handler = "${local.handler_ns}.SeedTenantMetadataFunction::Handle" }
    create-tenant-database = { handler = "${local.handler_ns}.CreateTenantDatabaseFunction::Handle" }
    create-tenant-tables   = { handler = "${local.handler_ns}.CreateTenantTablesFunction::Handle" }
    provision-cognito      = { handler = "${local.handler_ns}.ProvisionCognitoFunction::Handle" }
    create-admin-user      = { handler = "${local.handler_ns}.CreateAdminUserFunction::Handle" }
    configure-subdomain    = { handler = "${local.handler_ns}.ConfigureSubdomainFunction::Handle" }
    finalize-routing       = { handler = "${local.handler_ns}.FinalizeRoutingFunction::Handle" }
  }

  # Only the DB-touching steps run in-VPC (they reach Aurora over TCP). The Cognito/Route53
  # steps stay outside the VPC so they can reach those public/global AWS endpoints — this VPC
  # uses interface endpoints, not NAT, and Route53 has no PrivateLink endpoint.
  db_function_keys = ["seed-tenant-metadata", "create-tenant-database", "create-tenant-tables", "finalize-routing"]
  db_functions     = toset(local.db_function_keys)

  # All seven share one composition root, which reads every var at cold start — so every Lambda
  # gets the full env map regardless of which single step it runs.
  common_env = {
    THOR_MASTERDB_HOST                        = var.aurora_writer_endpoint
    THOR_MASTERDB_DATABASE                    = var.aurora_database_name
    THOR_MASTERDB_USER                        = var.metadata_writer_db_user
    THOR_MASTERDB_PORT                        = "5432"
    THOR_PROVISIONING_REGION                  = data.aws_region.current.region
    THOR_PROVISIONING_CLUSTER_WRITER_ENDPOINT = var.aurora_writer_endpoint
    THOR_PROVISIONING_ROUTING_ENDPOINT        = var.tenant_routing_endpoint
    THOR_PROVISIONING_DB_USER                 = var.provisioning_db_user
    THOR_PROVISIONING_ADMIN_DB                = "postgres"
    THOR_PROVISIONING_HOSTED_ZONE_ID          = var.hosted_zone_id
    THOR_PROVISIONING_BASE_DOMAIN             = var.base_domain
    THOR_PROVISIONING_DNS_TARGET              = var.dns_target
  }

  rds_db_arn_prefix = "arn:aws:rds-db:${data.aws_region.current.region}:${data.aws_caller_identity.current.account_id}:dbuser:${var.aurora_cluster_resource_id}"

  # Each DB Lambda connects as exactly one role — least privilege: only create-tenant-database
  # gets the DDL role; seed/finalize get the metadata-writer role. create-tenant-tables is
  # scoped separately (aws_iam_role_policy.rds_connect_create_tables) since its role is
  # per-tenant (tenant_<id>_rw), not one of these static, environment-wide roles.
  db_function_dbuser = {
    seed-tenant-metadata   = var.metadata_writer_db_user
    create-tenant-database = var.provisioning_db_user
    finalize-routing       = var.metadata_writer_db_user
  }

  # The ASL lives beside the publish dir (backend/functions/Thor.TenantProvisioning/).
  asl_path = "${dirname(var.source_dir)}/statemachine/tenant-provisioning.asl.json"
}

# Terraform zips a pre-published directory — it does not compile. The root.hcl before_hook runs
# dotnet publish into var.source_dir first (same pattern as modules/lambda).
# output_path uses dirname(), not "${var.source_dir}/..": CI's apply job restores the zip alone, never
# publish/, and a path routed through a directory that doesn't exist fails to open even when the file does.
data "archive_file" "provisioning" {
  type        = "zip"
  source_dir  = var.source_dir
  output_path = "${dirname(var.source_dir)}/tenant-provisioning-build.zip"
}

# --- provisioning security group (in-VPC DB Lambdas only) ---
# name_prefix + create_before_destroy: name/description/vpc_id are all ForceNew, and a destroy-first
# replacement cannot complete once this SG is attached to a Lambda — the group stays in-use until the
# service reaps the ENI, which only happens after the vpc_config update that Terraform orders behind
# the delete. Creating the replacement first lets the functions move off before the old group goes.
resource "aws_security_group" "provisioning" {
  name_prefix = "${local.name_prefix}-sg-"
  description = "Tenant provisioning DB Lambdas - egress to VPC only (reach Aurora on 5432)"
  vpc_id      = var.vpc_id

  egress {
    description = "Within VPC only"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = [var.vpc_cidr]
  }

  tags = merge(var.tags, { Name = "${local.name_prefix}-sg" })

  lifecycle {
    create_before_destroy = true
  }
}

# DDL + master writes go direct to the Aurora writer (not through a proxy), so the provisioning
# SG needs ingress to Aurora on 5432. Rule lives here (not in the aurora module) to avoid a
# module dependency cycle.
resource "aws_vpc_security_group_ingress_rule" "provisioning_to_aurora" {
  security_group_id            = var.aurora_security_group_id
  description                  = "PostgreSQL from tenant provisioning Lambdas"
  referenced_security_group_id = aws_security_group.provisioning.id
  from_port                    = 5432
  to_port                      = 5432
  ip_protocol                  = "tcp"

  tags = merge(var.tags, { Name = "${local.name_prefix}-aurora-ingress" })
}

# --- Lambda IAM: one role per function, least-privilege ---
data "aws_iam_policy_document" "lambda_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "fn" {
  for_each = local.functions

  name                 = "${local.name_prefix}-${each.key}-role"
  assume_role_policy   = data.aws_iam_policy_document.lambda_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags
}

resource "aws_cloudwatch_log_group" "fn" {
  for_each = local.functions

  name              = "/aws/lambda/${local.name_prefix}-${each.key}"
  retention_in_days = 30
  tags              = var.tags
}

# Base policy (all functions): write to own log group only — replaces AWSLambdaBasicExecutionRole.
resource "aws_iam_role_policy" "logs" {
  for_each = local.functions

  name = "logs"
  role = aws_iam_role.fn[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["logs:CreateLogStream", "logs:PutLogEvents"]
      Resource = "${aws_cloudwatch_log_group.fn[each.key].arn}:*"
    }]
  })
}

# VPC ENI management — only the in-VPC DB functions. These EC2 actions can't be resource-scoped.
resource "aws_iam_role_policy" "vpc" {
  for_each = local.db_functions

  name = "vpc-access"
  role = aws_iam_role.fn[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "ec2:CreateNetworkInterface",
        "ec2:DescribeNetworkInterfaces",
        "ec2:DeleteNetworkInterface",
        "ec2:AssignPrivateIpAddresses",
        "ec2:UnassignPrivateIpAddresses",
      ]
      Resource = "*"
    }]
  })
}

# rds-db:connect — each DB function scoped to its own dbuser (DDL vs metadata-writer).
resource "aws_iam_role_policy" "rds_connect" {
  for_each = local.db_function_dbuser

  name = "rds-connect"
  role = aws_iam_role.fn[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["rds-db:connect"]
      Resource = "${local.rds_db_arn_prefix}/${each.value}"
    }]
  })
}

# create-tenant-tables connects as the tenant's own _rw role (see
# PostgresTenantSchemaMigrator), not a static dbuser — the role name is per-tenant
# (tenant_<id>_rw, deterministic from ProvisioningNames-style naming) and unknown at apply
# time, so this is scoped by wildcard instead of joining db_function_dbuser.
resource "aws_iam_role_policy" "rds_connect_create_tables" {
  name = "rds-connect"
  role = aws_iam_role.fn["create-tenant-tables"].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["rds-db:connect"]
      Resource = "${local.rds_db_arn_prefix}/tenant_*_rw"
    }]
  })
}

# Cognito pool/group setup — provision-cognito only. Pools are created at runtime, so the
# create/list actions can't be scoped to a pool ARN.
resource "aws_iam_role_policy" "cognito_pool" {
  name = "cognito-pool"
  role = aws_iam_role.fn["provision-cognito"].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "cognito-idp:CreateUserPool",
        "cognito-idp:CreateUserPoolClient",
        "cognito-idp:CreateGroup",
        "cognito-idp:ListUserPools",
        "cognito-idp:ListUserPoolClients",
        "cognito-idp:DescribeUserPool",
      ]
      Resource = "*"
    }]
  })
}

# Admin user creation — create-admin-user only.
resource "aws_iam_role_policy" "cognito_user" {
  name = "cognito-user"
  role = aws_iam_role.fn["create-admin-user"].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["cognito-idp:AdminCreateUser", "cognito-idp:AdminAddUserToGroup"]
      Resource = "*"
    }]
  })
}

# Route53 record management — configure-subdomain only, scoped to the tenant hosted zone.
resource "aws_iam_role_policy" "route53" {
  name = "route53-change"
  role = aws_iam_role.fn["configure-subdomain"].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["route53:ChangeResourceRecordSets", "route53:ListResourceRecordSets"]
      Resource = "arn:aws:route53:::hostedzone/${var.hosted_zone_id}"
    }]
  })
}

resource "aws_lambda_function" "fn" {
  for_each = local.functions

  function_name = "${local.name_prefix}-${each.key}"
  role          = aws_iam_role.fn[each.key].arn
  runtime       = var.runtime
  handler       = each.value.handler
  timeout       = var.timeout
  memory_size   = var.memory_size

  filename         = data.archive_file.provisioning.output_path
  source_code_hash = data.archive_file.provisioning.output_base64sha256

  environment {
    variables = local.common_env
  }

  # DB functions run in-VPC to reach Aurora; the rest stay outside for public/global endpoints.
  dynamic "vpc_config" {
    for_each = contains(local.db_function_keys, each.key) ? [1] : []
    content {
      subnet_ids         = var.private_subnet_ids
      security_group_ids = [aws_security_group.provisioning.id]
    }
  }

  depends_on = [aws_cloudwatch_log_group.fn, aws_iam_role_policy.logs]

  tags = var.tags
}

# --- Step Functions state machine ---
data "aws_iam_policy_document" "sfn_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["states.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "sfn" {
  name                 = "${local.name_prefix}-sfn-role"
  assume_role_policy   = data.aws_iam_policy_document.sfn_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags
}

resource "aws_iam_role_policy" "sfn_invoke" {
  name = "invoke-provisioning-lambdas"
  role = aws_iam_role.sfn.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["lambda:InvokeFunction"]
      Resource = [for f in aws_lambda_function.fn : f.arn]
    }]
  })
}

resource "aws_sfn_state_machine" "provisioning" {
  name     = local.name_prefix
  role_arn = aws_iam_role.sfn.arn

  definition = templatefile(local.asl_path, {
    SeedTenantMetadataFunctionArn   = aws_lambda_function.fn["seed-tenant-metadata"].arn
    CreateTenantDatabaseFunctionArn = aws_lambda_function.fn["create-tenant-database"].arn
    CreateTenantTablesFunctionArn   = aws_lambda_function.fn["create-tenant-tables"].arn
    ProvisionCognitoFunctionArn     = aws_lambda_function.fn["provision-cognito"].arn
    CreateAdminUserFunctionArn      = aws_lambda_function.fn["create-admin-user"].arn
    ConfigureSubdomainFunctionArn   = aws_lambda_function.fn["configure-subdomain"].arn
    FinalizeRoutingFunctionArn      = aws_lambda_function.fn["finalize-routing"].arn
  })

  tags = var.tags
}

# The workflow is request-driven and terminates in a Fail state on error (the tenant stays
# status=Provisioning for inspection), so there is no async event source to dead-letter — the
# operational signal is a failed execution. Alarm on it; wire alarm_actions to an SNS topic to
# get notified.
resource "aws_cloudwatch_metric_alarm" "executions_failed" {
  alarm_name          = "${local.name_prefix}-executions-failed"
  namespace           = "AWS/States"
  metric_name         = "ExecutionsFailed"
  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  alarm_description   = "A tenant-provisioning execution failed after retries — inspect the execution history."
  alarm_actions       = var.alarm_actions

  dimensions = {
    StateMachineArn = aws_sfn_state_machine.provisioning.arn
  }

  tags = var.tags
}
