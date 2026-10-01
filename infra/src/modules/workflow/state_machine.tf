locals {
  has_lambda = length(local.lambda_step_names) > 0
  has_ecs    = length(local.ecs_step_names) > 0

  # Built by hand rather than referenced: the policy is attached before the state machine exists.
  state_machine_arn = "arn:aws:states:${var.aws_region}:${var.account_id}:stateMachine:${local.name_prefix}-sf"
}

resource "aws_cloudwatch_log_group" "state_machine" {
  count = local.active ? 1 : 0

  name              = "/aws/states/${local.name_prefix}"
  retention_in_days = var.log_retention_days
  tags              = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

data "aws_iam_policy_document" "state_machine_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["states.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "state_machine" {
  count = local.active ? 1 : 0

  name                 = "${local.name_prefix}-sf"
  assume_role_policy   = data.aws_iam_policy_document.state_machine_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# One policy for everything the state machine needs. Each block is gated on the workflow actually
# using that integration, so a Lambda-only workflow gets no ECS grants and a workflow with no map
# step gets no results-bucket write.
data "aws_iam_policy_document" "state_machine" {
  count = local.active ? 1 : 0

  dynamic "statement" {
    for_each = local.has_lambda ? [1] : []

    content {
      actions   = ["lambda:InvokeFunction"]
      resources = [for n in local.lambda_step_names : aws_lambda_function.step[n].arn]
    }
  }

  dynamic "statement" {
    for_each = local.has_ecs ? [1] : []

    content {
      actions   = ["ecs:RunTask"]
      resources = [for n in local.ecs_step_names : aws_ecs_task_definition.step[n].arn]
    }
  }

  # Required for the .sync ECS integration: Step Functions auto-manages a hidden, fixed-name
  # EventBridge rule to detect when the task stops.
  dynamic "statement" {
    for_each = local.has_ecs ? [1] : []

    content {
      actions   = ["events:PutTargets", "events:PutRule", "events:DescribeRule"]
      resources = ["arn:aws:events:*:*:rule/StepFunctionsGetEventsForECSTaskRule"]
    }
  }

  # ecs:StopTask/DescribeTasks aren't resource-scopable — the task ARN doesn't exist until RunTask
  # creates it, so "*" is required.
  dynamic "statement" {
    for_each = local.has_ecs ? [1] : []

    content {
      actions   = ["ecs:StopTask", "ecs:DescribeTasks"]
      resources = ["*"]
    }
  }

  # Step Functions passes the execution role plus whichever task role the step's definition names.
  dynamic "statement" {
    for_each = local.has_ecs ? [1] : []

    content {
      actions = ["iam:PassRole"]
      resources = concat(
        [aws_iam_role.execution[0].arn],
        [for variant in local.role_variant_names : aws_iam_role.task[variant].arn],
      )
    }
  }

  # A Distributed Map runs each item as a child execution of this same state machine, so it needs to
  # start, inspect and stop its own executions — whether or not it also writes results to S3.
  dynamic "statement" {
    for_each = local.has_distributed_map ? [1] : []

    content {
      actions   = ["states:StartExecution"]
      resources = [local.state_machine_arn]
    }
  }

  dynamic "statement" {
    for_each = local.has_distributed_map ? [1] : []

    content {
      actions   = ["states:DescribeExecution", "states:StopExecution"]
      resources = ["arn:aws:states:${var.aws_region}:${var.account_id}:execution:${local.name_prefix}-sf:*"]
    }
  }

  # Only for a Map that declares a ResultWriter. One whose item outputs come back inline — because a
  # later state reads them — has no bucket to write to.
  dynamic "statement" {
    for_each = local.has_map_results ? [1] : []

    content {
      actions   = ["s3:PutObject"]
      resources = ["${aws_s3_bucket.map_results[0].arn}/*"]
    }
  }

  # Only for a definition that sends to a dead-letter queue. One that records its failures in its own
  # workflow row has no queue to grant against.
  dynamic "statement" {
    for_each = local.has_dlq ? [1] : []

    content {
      actions   = ["sqs:SendMessage"]
      resources = [aws_sqs_queue.dlq[0].arn]
    }
  }

  # Chained workflows. The optimized (non-.sync) startExecution integration returns as soon as the
  # child execution starts, so StartExecution is the whole grant — no DescribeExecution, and nothing
  # that would let this state machine observe or stop the child. Each workflow owns its own failure
  # handling.
  dynamic "statement" {
    for_each = length(local.chained_state_machine_arns) > 0 ? [1] : []

    content {
      actions   = ["states:StartExecution"]
      resources = values(local.chained_state_machine_arns)
    }
  }

  # Account-level log-delivery API the logging integration requires — not resource-scopable.
  statement {
    actions = [
      "logs:CreateLogDelivery",
      "logs:GetLogDelivery",
      "logs:UpdateLogDelivery",
      "logs:DeleteLogDelivery",
      "logs:ListLogDeliveries",
      "logs:PutResourcePolicy",
      "logs:DescribeResourcePolicies",
      "logs:DescribeLogGroups",
    ]
    resources = ["*"]
  }
}

resource "aws_iam_role_policy" "state_machine" {
  count = local.active ? 1 : 0

  name   = "${local.name_prefix}-state-machine-permissions"
  role   = aws_iam_role.state_machine[0].id
  policy = data.aws_iam_policy_document.state_machine[0].json
}

# The definition is authored in asl/<name>.asl.json and filled in by templatefile. Nothing here is per-step,
# which is the point: adding a step is one list entry, and the two compute branches cannot drift
# because they are produced from the same template.
#
# The task-definition ARNs it pins are revision-qualified. That was a defect before — CI registered
# revisions the state machine could never reach — and is correct now only because Terraform owns the
# image tag: a new tag produces a new revision and rewrites this definition in the same apply.
resource "aws_sfn_state_machine" "workflow" {
  count = local.active ? 1 : 0

  name       = "${local.name_prefix}-sf"
  role_arn   = aws_iam_role.state_machine[0].arn
  definition = templatefile(local.asl_path, local.asl_vars)

  logging_configuration {
    log_destination        = "${aws_cloudwatch_log_group.state_machine[0].arn}:*"
    include_execution_data = true
    level                  = "ALL"
  }

  tags = var.tags

  depends_on = [aws_iam_role_policy.state_machine]

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
