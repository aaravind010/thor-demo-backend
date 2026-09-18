resource "aws_cloudwatch_log_group" "state_machine_log_group" {
  count = local.ingestion_active ? 1 : 0

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
  count = local.ingestion_active ? 1 : 0

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
  count = local.ingestion_active ? 1 : 0

  statement {
    actions   = ["ecs:RunTask"]
    resources = [for step in local.ingestion_steps : aws_ecs_task_definition.ecs_ingestion_task_definition[step].arn]
  }

  # ecs:StopTask/DescribeTasks aren't resource-scopable — the task ARN doesn't exist until RunTask
  # creates it, so "*" is required.
  statement {
    actions   = ["ecs:StopTask", "ecs:DescribeTasks"]
    resources = ["*"]
  }

  # Step Functions passes the execution role plus whichever task role the step's task definition
  # names (ingestion_task_role, or ingestion_graph_load_task_role for local.graph_load_steps).
  statement {
    actions = ["iam:PassRole"]
    resources = [
      aws_iam_role.ingestion_execution_role[0].arn,
      aws_iam_role.ingestion_task_role[0].arn,
      aws_iam_role.ingestion_graph_load_task_role[0].arn,
    ]
  }

  # Required for the .sync ECS integration: Step Functions auto-manages a hidden, fixed-name
  # EventBridge rule to detect when the task stops.
  statement {
    actions   = ["events:PutTargets", "events:PutRule", "events:DescribeRule"]
    resources = ["arn:aws:events:*:*:rule/StepFunctionsGetEventsForECSTaskRule"]
  }

  statement {
    actions   = ["sqs:SendMessage"]
    resources = [aws_sqs_queue.sqs_ingestion_dlq[0].arn]
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
  count = local.ingestion_active ? 1 : 0

  name   = "${local.name_prefix}-state-machine-permissions"
  role   = aws_iam_role.state_machine_role[0].id
  policy = data.aws_iam_policy_document.state_machine_permissions[0].json
}

# Runs Thor.Workflows.Ingestion's 4 steps in order (extract-stage -> promote -> graph-load-start
# -> graph-load-poll), each its own ECS task carrying the same IngestionRequest payload unchanged.
# Every step is independently idempotent (see docs/architecture/ingestion_workflow.md on thor-50),
# so a failed step retries in place before escalating straight to the DLQ — no whole-pipeline restart.
resource "aws_sfn_state_machine" "sfn_ingestion" {
  count = local.ingestion_active ? 1 : 0

  name     = "${local.name_prefix}-sf"
  role_arn = aws_iam_role.state_machine_role[0].arn

  definition = jsonencode({
    Comment = "Ingestion workflow: extract-stage -> promote -> graph-load-start -> graph-load-poll, each retried in place before escalating to the DLQ."
    StartAt = "RunExtractAndStage"
    States = {
      RunExtractAndStage = merge(local.ingestion_task_states["extract-stage"], { Next = "RunPromote" })
      RunPromote         = merge(local.ingestion_task_states["promote"], { Next = "RunGraphLoadStart" })
      RunGraphLoadStart  = merge(local.ingestion_task_states["graph-load-start"], { Next = "RunGraphLoadPoll" })
      RunGraphLoadPoll   = merge(local.ingestion_task_states["graph-load-poll"], { Next = "WorkflowSucceeded" })

      SendToDeadLetterQueue = {
        Type     = "Task"
        Resource = "arn:aws:states:::sqs:sendMessage"
        Parameters = {
          QueueUrl        = aws_sqs_queue.sqs_ingestion_dlq[0].id
          "MessageBody.$" = "$"
        }
        End = true
      }

      WorkflowSucceeded = {
        Type = "Succeed"
      }
    }
  })

  logging_configuration {
    log_destination        = "${aws_cloudwatch_log_group.state_machine_log_group[0].arn}:*"
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
