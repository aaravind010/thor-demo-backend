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
    actions = ["lambda:InvokeFunction"]
    resources = concat(
      [aws_lambda_function.ingestion_driver[0].arn],
      [for step in local.ingestion_steps : aws_lambda_function.ingestion_step[step].arn],
    )
  }

  statement {
    actions   = ["ecs:RunTask"]
    resources = [for step in local.ingestion_steps : aws_ecs_task_definition.ecs_ingestion_task_definition[step].arn]
  }

  # Distributed Map runs each item as a child execution of this same state machine, so it needs to
  # start/inspect/stop its own executions. The state machine ARN is built by hand rather than
  # referenced — the policy is attached before the state machine exists.
  statement {
    actions   = ["states:StartExecution"]
    resources = ["arn:aws:states:${var.aws_region}:${var.account_id}:stateMachine:${local.name_prefix}-sf"]
  }

  statement {
    actions   = ["states:DescribeExecution", "states:StopExecution"]
    resources = ["arn:aws:states:${var.aws_region}:${var.account_id}:execution:${local.name_prefix}-sf:*"]
  }

  # The Map's ResultWriter.
  statement {
    actions   = ["s3:PutObject"]
    resources = ["${aws_s3_bucket.s3_map_results[0].arn}/*"]
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

# Thor.Workflows.IngestionDriver decides the compute target, then Thor.Workflows.Ingestion's 4 steps
# run in order (extract-stage -> promote -> graph-load-start -> graph-load-poll) on that target —
# Lambda or ECS Fargate, same image either way. extract-stage is two states against the same step:
# a list-mode invocation (always Lambda — ecs:runTask.sync has no return-value channel for the
# file list) then a Distributed Map fanning out per file. Every step is independently idempotent
# (docs/architecture/ingestion_workflow.md), so a failed step retries in place before escalating
# straight to the DLQ — no whole-pipeline restart. Expected execution input is CreateManifest's:
# { TenantId, ExportLocation, ScanId, ScanManifestId, BatchSeq, RunId } — RunId must be present
# (null is fine), since "$.RunId" on a missing path is an uncatchable States.Runtime error.
resource "aws_sfn_state_machine" "sfn_ingestion" {
  count = local.ingestion_active ? 1 : 0

  name     = "${local.name_prefix}-sf"
  role_arn = aws_iam_role.state_machine_role[0].arn

  definition = jsonencode({
    Comment = "Ingestion workflow: driver picks lambda|ecs-task, then extract-stage (list + per-file Distributed Map) -> promote -> graph-load-start -> graph-load-poll on that target, each retried in place before escalating to the DLQ."
    StartAt = "DriverInvoke"
    States = {
      # Sums the manifest's file sizes and returns ComputeTarget ("lambda" | "ecs-task") for
      # ChooseComputeTarget to read (ComputeSelectionResult.cs).
      DriverInvoke = {
        Type     = "Task"
        Resource = "arn:aws:states:::lambda:invoke"
        Parameters = {
          FunctionName = aws_lambda_function.ingestion_driver[0].arn
          Payload = {
            "TenantId.$"       = "$.TenantId"
            "ExportLocation.$" = "$.ExportLocation"
            "ScanId.$"         = "$.ScanId"
            "ScanManifestId.$" = "$.ScanManifestId"
          }
        }
        ResultSelector = {
          "ComputeTarget.$"  = "$.Payload.ComputeTarget"
          "TotalSizeBytes.$" = "$.Payload.TotalSizeBytes"
          "FileCount.$"      = "$.Payload.FileCount"
        }
        ResultPath = "$.Driver"
        Retry      = [local.step_retry]
        Catch = [
          {
            ErrorEquals = ["States.ALL"]
            ResultPath  = "$.Error"
            Next        = "SendToDeadLetterQueue"
          }
        ]
        Next = "ListFiles"
      }

      # extract-stage, list mode (FileLocation omitted): ExtractAndStageStep reads
      # ScanManifest.FileLocations and returns one IngestionRequest per file, which lands in
      # $.ListResult.Files and feeds whichever Map runs next — in-memory, no S3 for this handoff.
      ListFiles = merge(local.ingestion_lambda_states["extract-stage"], {
        Parameters = {
          FunctionName = aws_lambda_function.ingestion_step["extract-stage"].arn
          Payload = {
            "TenantId.$"       = "$.TenantId"
            "ExportLocation.$" = "$.ExportLocation"
            "ScanId.$"         = "$.ScanId"
            "ScanManifestId.$" = "$.ScanManifestId"
            "RunId.$"          = "$.RunId"
          }
        }
        ResultSelector = {
          "FileCount.$" = "$.Payload.FileCount"
          "Files.$"     = "$.Payload.Files"
        }
        ResultPath = "$.ListResult"
        Next       = "ChooseComputeTarget"
      })

      # After ListFiles rather than before it: listing doesn't depend on the target, and both
      # branches fan out over the same $.ListResult.Files, differing only in what runs per file.
      ChooseComputeTarget = {
        Type = "Choice"
        Choices = [
          { Variable = "$.Driver.ComputeTarget", StringEquals = "lambda", Next = "LambdaExtractStageMap" },
          { Variable = "$.Driver.ComputeTarget", StringEquals = "ecs-task", Next = "EcsExtractStageMap" },
        ]
        Default = "UnknownComputeTargetFailed"
      }

      UnknownComputeTargetFailed = {
        Type  = "Fail"
        Error = "UnknownComputeTarget"
        Cause = "Driver returned a ComputeTarget other than 'lambda' or 'ecs-task' — see ComputeTarget.cs."
      }

      # --- lambda branch ---
      LambdaExtractStageMap   = merge(local.extract_stage_map["lambda"], { Next = "LambdaBuildPromoteInput" })
      LambdaBuildPromoteInput = merge(local.build_promote_input, { Next = "LambdaPromote" })
      LambdaPromote           = merge(local.ingestion_lambda_states["promote"], { ResultPath = "$.Promote", Next = "LambdaGraphLoadStart" })
      LambdaGraphLoadStart    = merge(local.ingestion_lambda_states["graph-load-start"], { ResultPath = "$.GraphLoadStart", Next = "LambdaGraphLoadPoll" })

      # GraphLoadPollResult.IsInProgress comes back as a plain JSON field (LambdaEntry returns the
      # step's result object directly), so LambdaCheckPoll can branch on it cleanly.
      LambdaGraphLoadPoll = merge(local.ingestion_lambda_states["graph-load-poll"], {
        ResultSelector = {
          "Outcome.$"      = "$.Payload.Outcome"
          "LoadId.$"       = "$.Payload.LoadId"
          "WorkflowId.$"   = "$.Payload.WorkflowId"
          "IsInProgress.$" = "$.Payload.IsInProgress"
        }
        ResultPath = "$.Poll"
        Next       = "LambdaCheckPoll"
      })

      LambdaCheckPoll = {
        Type = "Choice"
        Choices = [
          { Variable = "$.Poll.IsInProgress", BooleanEquals = true, Next = "LambdaWaitBeforePoll" },
        ]
        Default = "IngestionSucceeded"
      }

      LambdaWaitBeforePoll = {
        Type    = "Wait"
        Seconds = var.graph_load_poll_interval_seconds
        Next    = "LambdaGraphLoadPoll"
      }

      # --- ecs-task branch ---
      EcsExtractStageMap   = merge(local.extract_stage_map["ecs-task"], { Next = "EcsBuildPromoteInput" })
      EcsBuildPromoteInput = merge(local.build_promote_input, { Next = "EcsPromote" })
      EcsPromote           = merge(local.ingestion_task_states["promote"], { Next = "EcsGraphLoadStart" })
      EcsGraphLoadStart    = merge(local.ingestion_task_states["graph-load-start"], { Next = "EcsGraphLoadPoll" })

      # graph-load-poll stays on ECS here rather than falling back to Lambda. ecs:runTask.sync only
      # exposes success vs States.TaskFailed from the exit code — it can't tell
      # WorkflowHost.RetryLaterExitCode ("still polling") from a genuine failure the way the Lambda
      # branch's IsInProgress field can, and this deliberately doesn't parse that back out of the
      # Cause string. So this is a plain bounded Retry (fixed interval, no in-progress branching):
      # re-invoke the poll a fixed number of times and, if the load still hasn't finished, treat it
      # as failed via Catch -> DLQ. Not a true wait-until-done loop.
      EcsGraphLoadPoll = merge(local.ingestion_task_states["graph-load-poll"], {
        Retry = [
          {
            ErrorEquals     = ["States.ALL"]
            IntervalSeconds = var.graph_load_poll_interval_seconds
            MaxAttempts     = var.graph_load_poll_max_attempts
            BackoffRate     = 1
          }
        ]
        Next = "IngestionSucceeded"
      })

      # --- shared terminal states ---
      # The one DLQ every top-level step's Catch sends to on retry exhaustion. (The per-file Map
      # ItemProcessors keep a local copy targeting the same queue — see locals.tf.)
      SendToDeadLetterQueue = {
        Type     = "Task"
        Resource = "arn:aws:states:::sqs:sendMessage"
        Parameters = {
          QueueUrl = aws_sqs_queue.sqs_ingestion_dlq[0].id
          MessageBody = {
            "TenantId.$"       = "$.TenantId"
            "ScanId.$"         = "$.ScanId"
            "ScanManifestId.$" = "$.ScanManifestId"
            "RunId.$"          = "$.RunId"
            "Error.$"          = "$.Error"
          }
        }
        ResultPath = null
        # Same policy as every other state — without it a transient SQS error would leave the failure
        # recorded only in the execution history, never in the queue.
        Retry = [local.step_retry]
        Next  = "IngestionFailed"
      }

      IngestionSucceeded = {
        Type = "Succeed"
      }

      IngestionFailed = {
        Type  = "Fail"
        Error = "IngestionFailed"
        Cause = "See the DLQ message SendToDeadLetterQueue just sent for which step failed and why."
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
