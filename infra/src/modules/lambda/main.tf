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
  name_prefix = "thor-${var.environment}-api-key-authorizer"
}

# source_dir/output_path are absolute (via get_repo_root()) since Terragrunt only copies infra/src, not backend/. Requires dotnet publish into authorizer_source_dir first — Terraform zips, it doesn't compile.
data "archive_file" "thor-authorizer-archive-file" {
  type        = "zip"
  source_dir  = var.authorizer_source_dir
  output_path = "${var.authorizer_source_dir}/../thor-authorizer-build.zip"
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
  name                 = "${local.name_prefix}-role"
  assume_role_policy   = data.aws_iam_policy_document.thor-lambda-authorizer-assume-policy-document.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags
}

resource "aws_cloudwatch_log_group" "thor-lambda-authorizer-logs" {
  name              = "/aws/lambda/${local.name_prefix}"
  retention_in_days = 30
  tags              = var.tags
}

# One role, one inline policy: the logs statement replaces the AWSLambdaBasicExecutionRole managed policy, scoped to just this function's log group instead of every log group in the account. rds-data:ExecuteStatement looks up the key via Aurora Data API; secretsmanager:GetSecretValue covers both Aurora's own secret (Data API auth) and the shared PBKDF2 salt secret SecretsManagerSaltProvider fetches at runtime.
data "aws_iam_policy_document" "thor-lambda-authorizer-permissions" {
  statement {
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
    resources = ["${aws_cloudwatch_log_group.thor-lambda-authorizer-logs.arn}:*"]
  }

  statement {
    actions   = ["rds-data:ExecuteStatement"]
    resources = [var.aurora_cluster_arn]
  }

  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [var.aurora_secret_arn, var.authorizer_salt_secret_arn]
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

  environment {
    variables = {
      AURORA_CLUSTER_ARN             = var.aurora_cluster_arn
      AURORA_SECRET_ARN              = var.aurora_secret_arn
      MASTER_DB_NAME                 = var.aurora_database_name
      THOR_AUTHORIZER_SALT_SECRET_ID = var.authorizer_salt_secret_arn
    }
  }

  depends_on = [aws_cloudwatch_log_group.thor-lambda-authorizer-logs, aws_iam_role_policy.thor_lambda_authorizer_permissions]

  tags = var.tags
}
