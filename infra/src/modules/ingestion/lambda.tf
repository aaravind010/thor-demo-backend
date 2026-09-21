# source_dir is absolute since Terragrunt only copies infra/src, not backend/. Requires dotnet
# publish into create_manifest_source_dir first — Terraform zips, it doesn't compile.
data "archive_file" "manifest_lambda_archive" {
  type        = "zip"
  source_dir  = var.create_manifest_source_dir
  output_path = "${var.create_manifest_source_dir}/../create-manifest-build.zip"
}

data "aws_iam_policy_document" "create_manifest_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "manifest_lambda_role" {
  count = local.ingestion_active ? 1 : 0

  name                 = "${local.name_prefix}-create-manifest"
  assume_role_policy   = data.aws_iam_policy_document.create_manifest_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_iam_role_policy_attachment" "manifest_lambda_policy_attachment" {
  count = local.ingestion_active ? 1 : 0

  role       = aws_iam_role.manifest_lambda_role[0].name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

# CreateManifest itself starts the Step Functions execution. One policy for everything beyond
# basic execution — add statements here as needed, not a new policy resource.
data "aws_iam_policy_document" "manifest_lambda_role_permissions_document" {
  count = local.ingestion_active ? 1 : 0

  statement {
    actions   = ["states:StartExecution"]
    resources = [aws_sfn_state_machine.sfn_ingestion[0].arn]
  }
}

resource "aws_iam_role_policy" "manifest_lambda_role_policy" {
  count = local.ingestion_active ? 1 : 0

  name   = "${local.name_prefix}-manifest-permissions"
  role   = aws_iam_role.manifest_lambda_role[0].id
  policy = data.aws_iam_policy_document.manifest_lambda_role_permissions_document[0].json
}

resource "aws_cloudwatch_log_group" "manifest_lambda_log_group" {
  count = local.ingestion_active ? 1 : 0

  name              = "/aws/lambda/${local.name_prefix}-create-manifest"
  retention_in_days = var.log_retention_days
  tags              = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_lambda_function" "manifest_lambda_function" {
  count = local.ingestion_active ? 1 : 0

  function_name = "${local.name_prefix}-create-manifest"
  role          = aws_iam_role.manifest_lambda_role[0].arn
  runtime       = "dotnet10"
  # Matches CreateManifest's assembly/namespace/class (backend/functions/CreateManifest/src).
  handler          = "CreateManifest::CreateManifest.Function::FunctionHandler"
  filename         = data.archive_file.manifest_lambda_archive.output_path
  source_code_hash = data.archive_file.manifest_lambda_archive.output_base64sha256
  timeout          = var.create_manifest_timeout
  memory_size      = var.create_manifest_memory_size

  environment {
    variables = {
      STATE_MACHINE_ARN = aws_sfn_state_machine.sfn_ingestion[0].arn
    }
  }

  tags = var.tags

  depends_on = [
    aws_cloudwatch_log_group.manifest_lambda_log_group,
    aws_iam_role_policy_attachment.manifest_lambda_policy_attachment,
    aws_iam_role_policy.manifest_lambda_role_policy,
  ]

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
