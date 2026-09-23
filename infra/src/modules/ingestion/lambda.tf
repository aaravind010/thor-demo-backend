# The two zip-packaged .NET Lambdas: CreateManifest (starts the state machine) and IngestionDriver
# (its first state). Both run in-VPC on ingestion_task_sg so they reach the RDS Proxy the same way
# the ECS tasks do, and both read the Master DB via THOR_MASTERDB_*. source_dir is absolute since
# Terragrunt only copies infra/src, not backend/ — dotnet publish must have run first (Terraform
# zips, it doesn't compile; scripts/publish-lambda-functions.sh).

data "aws_iam_policy_document" "lambda_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

# --- CreateManifest ---

# output_path uses dirname(), not "${var.create_manifest_source_dir}/..": CI's apply job restores the zip
# alone, never publish/, and a path routed through a directory that doesn't exist fails to open even when
# the file does.
data "archive_file" "manifest_lambda_archive" {
  type        = "zip"
  source_dir  = var.create_manifest_source_dir
  output_path = "${dirname(var.create_manifest_source_dir)}/create-manifest-build.zip"
}

resource "aws_iam_role" "manifest_lambda_role" {
  count = local.ingestion_active ? 1 : 0

  name                 = "${local.name_prefix}-create-manifest"
  assume_role_policy   = data.aws_iam_policy_document.lambda_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# VPC variant of AWSLambdaBasicExecutionRole — adds the ENI management an in-VPC function needs.
resource "aws_iam_role_policy_attachment" "manifest_lambda_policy_attachment" {
  count = local.ingestion_active ? 1 : 0

  role       = aws_iam_role.manifest_lambda_role[0].name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

# CreateManifest itself starts the Step Functions execution, and reaches the Master and tenant DBs
# through the proxy with an RDS IAM token. One policy for everything beyond basic execution — add
# statements here as needed, not a new policy resource.
data "aws_iam_policy_document" "manifest_lambda_role_permissions_document" {
  count = local.ingestion_active ? 1 : 0

  statement {
    actions   = ["states:StartExecution"]
    resources = [aws_sfn_state_machine.sfn_ingestion[0].arn]
  }

  statement {
    actions   = ["rds-db:connect"]
    resources = local.rds_db_connect_resources
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
  # Matches Thor.CreateManifest.Function's assembly::namespace.class::method (its aws-lambda-tools-defaults.json).
  handler          = "Thor.CreateManifest.Function::Thor.CreateManifest.Function.Function::FunctionHandler"
  filename         = data.archive_file.manifest_lambda_archive.output_path
  source_code_hash = data.archive_file.manifest_lambda_archive.output_base64sha256
  timeout          = var.create_manifest_timeout
  memory_size      = var.create_manifest_memory_size

  # No NAT on these subnets: Step Functions is reached through modules/network's states interface
  # endpoint.
  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [aws_security_group.ingestion_task_sg[0].id]
  }

  # Thor.CreateManifest.Function/CompositionRoot.cs's contract.
  environment {
    variables = {
      THOR_INGESTION_STATE_MACHINE_ARN = aws_sfn_state_machine.sfn_ingestion[0].arn
      THOR_MASTERDB_HOST               = var.db_host
      THOR_MASTERDB_DATABASE           = var.db_name
      THOR_MASTERDB_USER               = var.master_db_app_user
      THOR_MASTERDB_REGION             = var.aws_region
      THOR_MASTERDB_PORT               = "5432"
      THOR_MASTERDB_USESSL             = "true"
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

# --- IngestionDriver ---

# output_path uses dirname() for the same reason as manifest_lambda_archive above.
data "archive_file" "driver_lambda_archive" {
  type        = "zip"
  source_dir  = var.ingestion_driver_source_dir
  output_path = "${dirname(var.ingestion_driver_source_dir)}/ingestion-driver-build.zip"
}

resource "aws_iam_role" "driver_lambda_role" {
  count = local.ingestion_active ? 1 : 0

  name                 = "${local.name_prefix}-driver"
  assume_role_policy   = data.aws_iam_policy_document.lambda_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_iam_role_policy_attachment" "driver_lambda_policy_attachment" {
  count = local.ingestion_active ? 1 : 0

  role       = aws_iam_role.driver_lambda_role[0].name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

# The driver sizes the manifest's files (s3:HeadObject is covered by GetObject) and, like every
# other consumer, reaches the Master and tenant DBs through the proxy with an RDS IAM token. One
# policy — add statements here, not a new resource.
data "aws_iam_policy_document" "driver_lambda_role_permissions_document" {
  count = local.ingestion_active ? 1 : 0

  statement {
    actions   = ["s3:GetObject", "s3:ListBucket"]
    resources = [aws_s3_bucket.s3_ingestion[0].arn, "${aws_s3_bucket.s3_ingestion[0].arn}/*"]
  }

  statement {
    actions   = ["rds-db:connect"]
    resources = local.rds_db_connect_resources
  }
}

resource "aws_iam_role_policy" "driver_lambda_role_policy" {
  count = local.ingestion_active ? 1 : 0

  name   = "${local.name_prefix}-driver-permissions"
  role   = aws_iam_role.driver_lambda_role[0].id
  policy = data.aws_iam_policy_document.driver_lambda_role_permissions_document[0].json
}

resource "aws_cloudwatch_log_group" "driver_lambda_log_group" {
  count = local.ingestion_active ? 1 : 0

  name              = "/aws/lambda/${local.name_prefix}-driver"
  retention_in_days = var.log_retention_days
  tags              = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_lambda_function" "ingestion_driver" {
  count = local.ingestion_active ? 1 : 0

  function_name = "${local.name_prefix}-driver"
  role          = aws_iam_role.driver_lambda_role[0].arn
  runtime       = "dotnet10"
  # Matches Thor.Workflows.IngestionDriver.Function's aws-lambda-tools-defaults.json.
  handler          = "Thor.Workflows.IngestionDriver.Function::Thor.Workflows.IngestionDriver.Function.Function::FunctionHandler"
  filename         = data.archive_file.driver_lambda_archive.output_path
  source_code_hash = data.archive_file.driver_lambda_archive.output_base64sha256
  timeout          = var.ingestion_driver_timeout
  memory_size      = var.ingestion_driver_memory_size

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [aws_security_group.ingestion_task_sg[0].id]
  }

  # Thor.Workflows.IngestionDriver.Function/CompositionRoot.cs's contract. Its
  # TenantConnectionManagerFactory mints an RDS IAM token for THOR_MASTERDB_USER in
  # THOR_MASTERDB_REGION — there is no password auth path.
  environment {
    variables = {
      THOR_INGESTION_DRIVER_MAX_BYTES = tostring(var.ingestion_driver_max_bytes)
      THOR_MASTERDB_HOST              = var.db_host
      THOR_MASTERDB_DATABASE          = var.db_name
      THOR_MASTERDB_USER              = var.master_db_app_user
      THOR_MASTERDB_REGION            = var.aws_region
      THOR_MASTERDB_PORT              = "5432"
      THOR_MASTERDB_USESSL            = "true"
    }
  }

  tags = var.tags

  depends_on = [
    aws_cloudwatch_log_group.driver_lambda_log_group,
    aws_iam_role_policy_attachment.driver_lambda_policy_attachment,
    aws_iam_role_policy.driver_lambda_role_policy,
  ]

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
