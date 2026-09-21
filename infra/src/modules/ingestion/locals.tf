locals {
  name_prefix      = "thor-${var.environment}-ingestion"
  ingestion_active = var.enable_ingestion
  bucket_name      = "${local.name_prefix}-${var.account_id}"

  # Where the Distributed Map's ResultWriter lands per-file results (state_machine.tf). Its own
  # bucket, not a prefix in bucket_name — that bucket's s3:ObjectCreated:* notification (main.tf)
  # would feed every result object straight back into the pipeline.
  map_results_bucket_name = "${local.name_prefix}-map-results-${var.account_id}"

  # One THOR_STEP per Thor.Workflows.Ingestion container entry point, in pipeline order —
  # see ecs_task.tf/lambda_steps.tf/state_machine.tf.
  ingestion_steps = ["extract-stage", "promote", "graph-load-start", "graph-load-poll"]

  # The steps that write back to the ingestion bucket. They run under their own task role
  # (ecs_task.tf's ingestion_graph_load_task_role) so s3:PutObject isn't granted to every step.
  graph_load_steps = ["graph-load-start", "graph-load-poll"]

  # Falls back to this module's own ECR repo at "latest" when unset.
  resolved_ingestion_image = var.ingestion_container_image != "" ? var.ingestion_container_image : "${aws_ecr_repository.ecr_ingestion.repository_url}:latest"

  # Env every Thor.Workflows.Ingestion entry point reads (Composition/TenantConnectionManagerFactory,
  # Steps/GraphLoadStartStep, Steps/GraphLoadPollStep) — one map shared by the ECS task definitions
  # (ecs_task.tf) and the per-step Lambda functions (lambda_steps.tf) so the two compute targets
  # can't drift. Master-DB credentials are the one per-target difference: ECS injects
  # THOR_MASTERDB_USER/PASSWORD through its secrets block; Lambda has no equivalent and gets
  # THOR_MASTERDB_SECRET_ARN instead. THOR_AWS_REGION is explicit because Fargate, unlike Lambda,
  # never sets AWS_REGION.
  workflow_environment = {
    THOR_MASTERDB_HOST               = var.db_host
    THOR_MASTERDB_DATABASE           = var.db_name
    THOR_MASTERDB_PORT               = "5432"
    THOR_MASTERDB_USESSL             = "true"
    THOR_AWS_REGION                  = var.aws_region
    THOR_NEPTUNE_ENDPOINT            = var.neptune_endpoint
    THOR_NEPTUNE_PORT                = "8182"
    THOR_NEPTUNE_ENABLESSL           = "true"
    THOR_GRAPH_BULKLOAD_BUCKET       = local.bucket_name
    THOR_GRAPH_BULKLOAD_IAM_ROLE_ARN = var.neptune_loader_role_arn
  }

  # Common ecs:runTask.sync Task-state shape, one per THOR_STEP — Next/ResultPath/Retry are merged
  # in per-state in state_machine.tf since they differ per state. Guarded by ingestion_active so this
  # never indexes the (empty when inactive) for_each task-definition map. THOR_STEP isn't overridden
  # here: each task definition already bakes in its own (ecs_task.tf).
  ingestion_task_states = local.ingestion_active ? {
    for step in local.ingestion_steps : step => {
      Type     = "Task"
      Resource = "arn:aws:states:::ecs:runTask.sync"
      Parameters = {
        Cluster        = aws_ecs_cluster.ecs_ingestion_cluster[0].name
        TaskDefinition = aws_ecs_task_definition.ecs_ingestion_task_definition[step].arn
        LaunchType     = "FARGATE"
        NetworkConfiguration = {
          AwsvpcConfiguration = {
            Subnets        = var.private_subnet_ids
            SecurityGroups = [aws_security_group.ingestion_task_sg[0].id]
            AssignPublicIp = "DISABLED"
          }
        }
        Overrides = {
          ContainerOverrides = [
            {
              Name = "${local.name_prefix}-container"
              Environment = [
                { "Name" : "THOR_INPUT", "Value.$" : "States.JsonToString($)" }
              ]
            }
          ]
        }
      }
      # Keeps the RunTask API response from overwriting the IngestionRequest payload every step needs.
      ResultPath = null
      Retry      = [local.step_retry]
      Catch = [
        {
          ErrorEquals = ["States.ALL"]
          ResultPath  = "$.Error"
          Next        = "SendToDeadLetterQueue"
        }
      ]
    }
  } : {}

  # Common lambda:invoke Task-state shape, one per THOR_STEP — the same image as the ECS branch,
  # invoked as a Lambda function (Program.cs picks LambdaEntry when AWS_LAMBDA_RUNTIME_API is set).
  # Payload defaults to the whole state; ListFiles/DriverInvoke override it in state_machine.tf.
  ingestion_lambda_states = local.ingestion_active ? {
    for step in local.ingestion_steps : step => {
      Type     = "Task"
      Resource = "arn:aws:states:::lambda:invoke"
      Parameters = {
        FunctionName = aws_lambda_function.ingestion_step[step].arn
        "Payload.$"  = "$"
      }
      Retry = [local.step_retry]
      Catch = [
        {
          ErrorEquals = ["States.ALL"]
          ResultPath  = "$.Error"
          Next        = "SendToDeadLetterQueue"
        }
      ]
    }
  } : {}

  # The one retry policy every state gets before its Catch escalates to the DLQ — both compute
  # targets, both Map ItemProcessors, DriverInvoke, and SendToDeadLetterQueue itself. States.ALL
  # rather than just the Lambda service's transient errors: every step is idempotent (docs/
  # architecture/ingestion_workflow.md §10), so re-running a step's own failure is safe, and it keeps
  # the two branches' behaviour identical. The one exception is EcsGraphLoadPoll (state_machine.tf),
  # whose Retry is its polling loop and keeps its own cadence.
  step_retry = {
    ErrorEquals     = ["States.ALL"]
    IntervalSeconds = var.retry_interval_seconds
    MaxAttempts     = var.max_retry_count
    BackoffRate     = var.retry_backoff_rate
  }

  # extract-stage's Distributed Map, instantiated once per compute target in state_machine.tf with
  # that target's per-file Task state. Per-file failure is tolerated (docs/architecture/
  # ingestion_workflow.md §3): the item's Catch sends its identity to the shared DLQ, then reshapes
  # into a non-throwing Pass — the Map never sees a failed item, so no ToleratedFailure* is needed.
  # ItemProcessor is its own isolated sub-state-machine, so the DLQ send is a local copy targeting
  # the same queue, not a transition to the parent's SendToDeadLetterQueue. ResultPath is null and
  # ResultWriter goes to S3 so per-file volume never touches execution state size. null (not {})
  # when inactive: the two variants' item Tasks differ in shape, so {} isn't type-compatible.
  extract_stage_map = local.ingestion_active ? {
    for target, branch in {
      lambda = {
        prefix = ""
        task = merge(local.ingestion_lambda_states["extract-stage"], {
          ResultSelector = {
            "FileLocation.$"       = "$.Payload.FileLocation"
            "AccountsStaged.$"     = "$.Payload.AccountsStaged"
            "GroupsStaged.$"       = "$.Payload.GroupsStaged"
            "AssetsStaged.$"       = "$.Payload.AssetsStaged"
            "EntitlementsStaged.$" = "$.Payload.EntitlementsStaged"
          }
          ResultPath = "$.Result"
        })
      }
      ecs-task = {
        prefix = "Ecs"
        task   = local.ingestion_task_states["extract-stage"]
      }
      } : target => {
      Type           = "Map"
      Label          = "${branch.prefix == "" ? "Lambda" : branch.prefix}ExtractStageMap"
      ItemsPath      = "$.ListResult.Files"
      MaxConcurrency = var.map_max_concurrency
      ResultPath     = null
      ResultWriter = {
        Resource = "arn:aws:states:::s3:putObject"
        Parameters = {
          Bucket     = aws_s3_bucket.s3_map_results[0].bucket
          "Prefix.$" = "States.Format('ingestion-map-results/{}/{}', $.ScanManifestId, $$.Execution.Name)"
        }
      }
      ItemProcessor = {
        ProcessorConfig = { Mode = "DISTRIBUTED", ExecutionType = "STANDARD" }
        StartAt         = "${branch.prefix}ExtractFileTask"
        States = {
          "${branch.prefix}ExtractFileTask" = merge(branch.task, {
            Catch = [
              {
                ErrorEquals = ["States.ALL"]
                ResultPath  = "$.Error"
                Next        = "${branch.prefix}ExtractFileFailedNotifyDlq"
              }
            ]
            End = true
          })
          "${branch.prefix}ExtractFileFailedNotifyDlq" = {
            Type     = "Task"
            Resource = "arn:aws:states:::sqs:sendMessage"
            Parameters = {
              QueueUrl = aws_sqs_queue.sqs_ingestion_dlq[0].id
              MessageBody = {
                "TenantId.$"       = "$.TenantId"
                "ScanId.$"         = "$.ScanId"
                "ScanManifestId.$" = "$.ScanManifestId"
                "FileLocation.$"   = "$.FileLocation"
                "BatchSeq.$"       = "$.BatchSeq"
                "RunId.$"          = "$.RunId"
                FailedStep         = "extract-stage"
                ComputeTarget      = target
                "Error.$"          = "$.Error"
              }
            }
            ResultPath = null
            Retry      = [local.step_retry]
            Next       = "${branch.prefix}ExtractFileTolerated"
          }
          "${branch.prefix}ExtractFileTolerated" = {
            Type = "Pass"
            Parameters = {
              Success          = false
              "FileLocation.$" = "$.FileLocation"
              "Error.$"        = "$.Error"
            }
            End = true
          }
        }
      }
    }
  } : null

  # Drops $.ListResult (the per-file list the Map just iterated) out of execution state before
  # promote — TenantId/ExportLocation/ScanId/ScanManifestId/RunId are the same values the original
  # request already had. One per branch: the branches never reconverge before this point.
  build_promote_input = {
    Type = "Pass"
    Parameters = {
      "TenantId.$"       = "$.TenantId"
      "ExportLocation.$" = "$.ExportLocation"
      "ScanId.$"         = "$.ScanId"
      "ScanManifestId.$" = "$.ScanManifestId"
      "RunId.$"          = "$.RunId"
      "Driver.$"         = "$.Driver"
    }
    ResultPath = "$"
  }
}
