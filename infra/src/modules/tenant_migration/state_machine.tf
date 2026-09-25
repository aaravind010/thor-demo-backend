resource "aws_cloudwatch_log_group" "state_machine_log_group" {
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

resource "aws_iam_role" "state_machine_role" {
  name                 = "${local.name_prefix}-sf"
  assume_role_policy   = data.aws_iam_policy_document.state_machine_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# One policy for everything the state machine needs — add statements here as needed, not a new policy resource.
data "aws_iam_policy_document" "state_machine_permissions" {
  # Any revision of the family: CI registers a new one per runner image.
  statement {
    actions   = ["ecs:RunTask"]
    resources = ["arn:aws:ecs:${var.aws_region}:${var.account_id}:task-definition/${local.name_prefix}:*"]
  }

  # ecs:StopTask/DescribeTasks aren't resource-scopable — the task ARN doesn't exist until RunTask
  # creates it, so "*" is required.
  statement {
    actions   = ["ecs:StopTask", "ecs:DescribeTasks"]
    resources = ["*"]
  }

  statement {
    actions = ["iam:PassRole"]
    resources = [
      aws_iam_role.runner_execution_role.arn,
      aws_iam_role.runner_task_role.arn,
    ]
  }

  # Required for the .sync ECS integration: Step Functions auto-manages a hidden, fixed-name
  # EventBridge rule to detect when the task stops.
  statement {
    actions   = ["events:PutTargets", "events:PutRule", "events:DescribeRule"]
    resources = ["arn:aws:events:*:*:rule/StepFunctionsGetEventsForECSTaskRule"]
  }

  # WaitForPlanAndApproval (token object), ReadPlanResult, the Apply Map's ItemReader, MarkApplied.
  statement {
    actions   = ["s3:GetObject", "s3:PutObject"]
    resources = ["${aws_s3_bucket.migration_bucket.arn}/*"]
  }

  # Distributed Map runs each item as a child execution of this same state machine. The ARN is
  # built by hand — the policy is attached before the state machine exists.
  statement {
    actions   = ["states:StartExecution"]
    resources = ["arn:aws:states:${var.aws_region}:${var.account_id}:stateMachine:${local.name_prefix}"]
  }

  statement {
    actions   = ["states:DescribeExecution", "states:StopExecution"]
    resources = ["arn:aws:states:${var.aws_region}:${var.account_id}:execution:${local.name_prefix}:*"]
  }

  # Account-level log-delivery API Step Functions' logging integration requires — not resource-scopable.
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

resource "aws_iam_role_policy" "state_machine_policy" {
  name   = "${local.name_prefix}-state-machine-permissions"
  role   = aws_iam_role.state_machine_role.id
  policy = data.aws_iam_policy_document.state_machine_permissions.json
}

# Definition lives with the runner in migrations/tenant/statemachine (passed in as an absolute
# path, like tenant_provisioning's ASL — Terragrunt copies this module into its cache).
resource "aws_sfn_state_machine" "migration_state_machine" {
  name     = local.name_prefix
  role_arn = aws_iam_role.state_machine_role.arn

  definition = templatefile(var.state_machine_definition_path, {
    ClusterArn             = aws_ecs_cluster.runner_cluster.arn
    Subnets                = jsonencode(var.private_subnet_ids)
    SecurityGroupId        = aws_security_group.runner_task_sg.id
    ContainerName          = local.container_name
    Bucket                 = aws_s3_bucket.migration_bucket.bucket
    ApprovalTimeoutSeconds = var.approval_timeout_seconds
    ApplyMaxConcurrency    = var.apply_max_concurrency
  })

  logging_configuration {
    log_destination        = "${aws_cloudwatch_log_group.state_machine_log_group.arn}:*"
    include_execution_data = true
    level                  = "ALL"
  }

  tags = var.tags

  depends_on = [aws_iam_role_policy.state_machine_policy]

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# A failed execution is what blocks the deploy; alarm on it so it isn't only visible in GitHub.
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
  alarm_description   = "A tenant schema migration execution failed — inspect the execution history and runs/<runId>/ in the migration bucket."
  alarm_actions       = var.alarm_actions

  dimensions = {
    StateMachineArn = aws_sfn_state_machine.migration_state_machine.arn
  }

  tags = var.tags
}
