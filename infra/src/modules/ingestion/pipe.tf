data "aws_iam_policy_document" "pipe_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["pipes.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "pipe_role" {
  count = local.ingestion_active ? 1 : 0

  name                 = "${local.name_prefix}-pipe"
  assume_role_policy   = data.aws_iam_policy_document.pipe_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# One policy for everything the pipe needs — add statements here as needed, not a new policy resource.
data "aws_iam_policy_document" "pipe_permissions_document" {
  count = local.ingestion_active ? 1 : 0

  statement {
    actions   = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"]
    resources = [aws_sqs_queue.sqs_ingestion[0].arn]
  }

  statement {
    actions   = ["lambda:InvokeFunction"]
    resources = [aws_lambda_function.manifest_lambda_function[0].arn]
  }
}

resource "aws_iam_role_policy" "pipe_policy" {
  count = local.ingestion_active ? 1 : 0

  name   = "${local.name_prefix}-pipe-permissions"
  role   = aws_iam_role.pipe_role[0].id
  policy = data.aws_iam_policy_document.pipe_permissions_document[0].json
}

# Target: CreateManifest, invoked directly and synchronously.
resource "aws_pipes_pipe" "pipe_ingestion_pipes" {
  count = local.ingestion_active ? 1 : 0

  name     = "${local.name_prefix}-pipe"
  role_arn = aws_iam_role.pipe_role[0].arn
  source   = aws_sqs_queue.sqs_ingestion[0].arn
  target   = aws_lambda_function.manifest_lambda_function[0].arn

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

  depends_on = [aws_iam_role_policy.pipe_policy]

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
