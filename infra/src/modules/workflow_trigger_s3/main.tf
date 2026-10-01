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

# Turns an upload into a workflow execution: bucket notification -> SQS -> EventBridge Pipe ->
# handler Lambda -> StartExecution.
#
# The bucket is an input. modules/uploads owns it, it holds live tenant scan data presigned by
# Thor.TaskApi, and it must never be declared here — modules/ingestion declared a bucket resolving to
# the same name modules/uploads already creates, so the first enable_ingestion apply would have
# failed with BucketAlreadyOwnedByYou. infra/src/imports.tf misdiagnosed that as a lost state entry
# and would have put one physical bucket under two Terraform addresses.
#
# The handler is here rather than in modules/workflow because it is part of the trigger, not the
# workflow: it runs before an execution exists. When it is folded into the workflow image it becomes
# a THOR_STEP and this module's target changes to the state machine.

locals {
  active      = var.enabled
  name_prefix = "thor-${var.environment}-workflow-${var.name}"
}

# --- trigger queue ------------------------------------------------------------------------------

resource "aws_sqs_queue" "trigger" {
  count = local.active ? 1 : 0

  name                       = var.queue_name
  visibility_timeout_seconds = var.sqs_visibility_timeout_seconds

  redrive_policy = jsonencode({
    deadLetterTargetArn = var.dlq_arn
    maxReceiveCount     = var.sqs_max_receive_count
  })

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Required for S3 bucket notifications to reach this queue — without it, events silently never arrive.
data "aws_iam_policy_document" "trigger_queue" {
  count = local.active ? 1 : 0

  statement {
    actions   = ["sqs:SendMessage"]
    resources = [aws_sqs_queue.trigger[0].arn]

    principals {
      type        = "Service"
      identifiers = ["s3.amazonaws.com"]
    }

    condition {
      test     = "ArnEquals"
      variable = "aws:SourceArn"
      values   = [var.bucket_arn]
    }
  }
}

resource "aws_sqs_queue_policy" "trigger" {
  count = local.active ? 1 : 0

  queue_url = aws_sqs_queue.trigger[0].id
  policy    = data.aws_iam_policy_document.trigger_queue[0].json
}

# --- notification -------------------------------------------------------------------------------
#
# filter_prefix is the whole reason this is a separate module rather than a block in modules/uploads:
# it is the boundary between "a tenant uploaded something" and "the pipeline wrote something".
# aws_s3_bucket_notification is authoritative for the entire bucket, so there must be exactly one.

resource "aws_s3_bucket_notification" "trigger" {
  count = local.active ? 1 : 0

  bucket = var.bucket_id

  queue {
    queue_arn     = aws_sqs_queue.trigger[0].arn
    events        = ["s3:ObjectCreated:*"]
    filter_prefix = var.filter_prefix
  }

  depends_on = [aws_sqs_queue_policy.trigger]
}

# --- handler ------------------------------------------------------------------------------------

data "aws_iam_policy_document" "handler_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

# output_path uses dirname(), not "${var.handler_source_dir}/..": CI's apply job restores the zip
# alone, never publish/, and a path routed through a directory that doesn't exist fails to open even
# when the file does.
data "archive_file" "handler" {
  type        = "zip"
  source_dir  = var.handler_source_dir
  output_path = "${dirname(var.handler_source_dir)}/${var.name}-trigger-build.zip"
}

resource "aws_iam_role" "handler" {
  count = local.active ? 1 : 0

  name                 = "${local.name_prefix}-trigger"
  assume_role_policy   = data.aws_iam_policy_document.handler_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# VPC variant of AWSLambdaBasicExecutionRole — adds the ENI management an in-VPC function needs.
resource "aws_iam_role_policy_attachment" "handler_vpc_access" {
  count = local.active ? 1 : 0

  role       = aws_iam_role.handler[0].name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

data "aws_iam_policy_document" "handler" {
  count = local.active ? 1 : 0

  statement {
    actions   = ["states:StartExecution"]
    resources = [var.state_machine_arn]
  }

  dynamic "statement" {
    for_each = var.handler_policy_statements

    content {
      actions   = statement.value.actions
      resources = statement.value.resources
    }
  }
}

resource "aws_iam_role_policy" "handler" {
  count = local.active ? 1 : 0

  name   = "${local.name_prefix}-trigger-permissions"
  role   = aws_iam_role.handler[0].id
  policy = data.aws_iam_policy_document.handler[0].json
}

resource "aws_cloudwatch_log_group" "handler" {
  count = local.active ? 1 : 0

  name              = "/aws/lambda/${local.name_prefix}-trigger"
  retention_in_days = var.log_retention_days
  tags              = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_lambda_function" "handler" {
  count = local.active ? 1 : 0

  function_name    = "${local.name_prefix}-trigger"
  role             = aws_iam_role.handler[0].arn
  runtime          = var.handler_runtime
  handler          = var.handler_entrypoint
  filename         = data.archive_file.handler.output_path
  source_code_hash = data.archive_file.handler.output_base64sha256
  timeout          = var.handler_timeout
  memory_size      = var.handler_memory_size

  # No NAT on these subnets: Step Functions is reached through modules/network's states interface endpoint.
  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [var.security_group_id]
  }

  environment {
    variables = merge(var.handler_environment, {
      (var.state_machine_env_var) = var.state_machine_arn
    })
  }

  tags = var.tags

  depends_on = [
    aws_cloudwatch_log_group.handler,
    aws_iam_role_policy_attachment.handler_vpc_access,
    aws_iam_role_policy.handler,
  ]

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# --- pipe -----------------------------------------------------------------------------------------

data "aws_iam_policy_document" "pipe_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["pipes.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "pipe" {
  count = local.active ? 1 : 0

  name                 = "${local.name_prefix}-pipe"
  assume_role_policy   = data.aws_iam_policy_document.pipe_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

data "aws_iam_policy_document" "pipe" {
  count = local.active ? 1 : 0

  statement {
    actions   = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"]
    resources = [aws_sqs_queue.trigger[0].arn]
  }

  statement {
    actions   = ["lambda:InvokeFunction"]
    resources = [aws_lambda_function.handler[0].arn]
  }
}

resource "aws_iam_role_policy" "pipe" {
  count = local.active ? 1 : 0

  name   = "${local.name_prefix}-pipe-permissions"
  role   = aws_iam_role.pipe[0].id
  policy = data.aws_iam_policy_document.pipe[0].json
}

resource "aws_pipes_pipe" "trigger" {
  count = local.active ? 1 : 0

  name     = "${local.name_prefix}-pipe"
  role_arn = aws_iam_role.pipe[0].arn
  source   = aws_sqs_queue.trigger[0].arn
  target   = aws_lambda_function.handler[0].arn

  source_parameters {
    sqs_queue_parameters {
      batch_size = var.pipe_batch_size
    }
  }

  target_parameters {
    lambda_function_parameters {
      invocation_type = "REQUEST_RESPONSE"
    }
  }

  depends_on = [aws_iam_role_policy.pipe]

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
