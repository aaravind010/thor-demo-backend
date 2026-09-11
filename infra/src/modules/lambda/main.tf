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
  name_prefix = "thor-${var.environment}-api-key-authorizer"

  rds_db_arn = "arn:aws:rds-db:${data.aws_region.current.region}:${data.aws_caller_identity.current.account_id}:dbuser:${var.aurora_cluster_resource_id}/${var.authorizer_db_user}"
}

# source_dir/output_path are absolute (via get_repo_root()) since Terragrunt only copies infra/src, not backend/. Requires dotnet publish into authorizer_source_dir first — Terraform zips, it doesn't compile.
data "archive_file" "thor-authorizer-archive-file" {
  type        = "zip"
  source_dir  = var.authorizer_source_dir
  output_path = "${var.authorizer_source_dir}/../thor-authorizer-build.zip"
}

# The authorizer runs in-VPC so it can reach the Master DB through the RDS Proxy over TCP. It
# resolves subdomain -> tenant routing with an RDS IAM token as the read-only thor_authorizer
# role — no password, no RDS Data API (the DB is never reachable from outside the VPC).
resource "aws_security_group" "authorizer" {
  name        = "${local.name_prefix}-sg"
  description = "Thor authorizer Lambda - egress to VPC only (reach the RDS Proxy on 5432)"
  vpc_id      = var.vpc_id

  egress {
    description = "Within VPC only"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = [var.vpc_cidr]
  }

  tags = merge(var.tags, { Name = "${local.name_prefix}-sg" })
}

# The authorizer connects through the RDS Proxy, so it needs ingress to the proxy on 5432. Rule
# lives here (not in the rds_proxy module) to avoid a module dependency cycle — same pattern as
# the tenant-provisioning module's ingress onto the Aurora SG.
resource "aws_vpc_security_group_ingress_rule" "authorizer_to_proxy" {
  security_group_id            = var.rds_proxy_security_group_id
  description                  = "PostgreSQL from the Thor authorizer Lambda"
  referenced_security_group_id = aws_security_group.authorizer.id
  from_port                    = 5432
  to_port                      = 5432
  ip_protocol                  = "tcp"

  tags = merge(var.tags, { Name = "${local.name_prefix}-proxy-ingress" })
}

data "aws_iam_policy_document" "thor-lambda-authorizer-assume-policy-document" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "thor-lambda-authorizer-role" {
  name               = "${local.name_prefix}-role"
  assume_role_policy = data.aws_iam_policy_document.thor-lambda-authorizer-assume-policy-document.json
  tags               = var.tags
}

resource "aws_cloudwatch_log_group" "thor-lambda-authorizer-logs" {
  name              = "/aws/lambda/${local.name_prefix}"
  retention_in_days = 30
  tags              = var.tags
}

# One role, one inline policy. The logs statement replaces the AWSLambdaBasicExecutionRole managed
# policy, scoped to just this function's log group. rds-db:connect (scoped to the thor_authorizer
# db-user) lets the Lambda mint an IAM token for the Master-DB routing lookup through the proxy;
# secretsmanager:GetSecretValue covers only the shared PBKDF2 salt secret the SecretsManagerSaltProvider
# fetches at runtime. The ec2 network-interface actions are the VPC-access permissions Lambda needs
# to attach an ENI in the private subnets (they cannot be resource-scoped).
data "aws_iam_policy_document" "thor-lambda-authorizer-permissions" {
  statement {
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
    resources = ["${aws_cloudwatch_log_group.thor-lambda-authorizer-logs.arn}:*"]
  }

  statement {
    actions   = ["rds-db:connect"]
    resources = [local.rds_db_arn]
  }

  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [var.authorizer_salt_secret_arn]
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

resource "aws_iam_role_policy" "thor_lambda_authorizer_permissions" {
  name   = "thor-lambda-authorizer-permissions"
  role   = aws_iam_role.thor-lambda-authorizer-role.id
  policy = data.aws_iam_policy_document.thor-lambda-authorizer-permissions.json
}

resource "aws_lambda_function" "thor-authorizer-lambda" {
  function_name = local.name_prefix
  role          = aws_iam_role.thor-lambda-authorizer-role.arn
  runtime       = var.runtime
  # Matches Thor.Authorizer's assembly/namespace/class (backend/functions/Thor.Authorizer/src).
  handler     = "Thor.Authorizer::Thor.Authorizer.Function::FunctionHandler"
  timeout     = var.timeout
  memory_size = var.memory_size

  filename         = data.archive_file.thor-authorizer-archive-file.output_path
  source_code_hash = data.archive_file.thor-authorizer-archive-file.output_base64sha256

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [aws_security_group.authorizer.id]
  }

  environment {
    variables = {
      THOR_MASTERDB_HOST             = var.rds_proxy_endpoint
      THOR_MASTERDB_DATABASE         = var.aurora_database_name
      THOR_MASTERDB_USER             = var.authorizer_db_user
      THOR_MASTERDB_REGION           = data.aws_region.current.region
      THOR_AUTHORIZER_SALT_SECRET_ID = var.authorizer_salt_secret_arn
    }
  }

  depends_on = [aws_cloudwatch_log_group.thor-lambda-authorizer-logs, aws_iam_role_policy.thor_lambda_authorizer_permissions]

  tags = var.tags
}
