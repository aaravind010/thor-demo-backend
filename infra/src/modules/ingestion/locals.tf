locals {
  name_prefix      = "thor-${var.environment}-ingestion"
  ingestion_active = var.enable_ingestion
  bucket_name      = "${local.name_prefix}-${var.account_id}"

  # One THOR_STEP per Thor.Workflows.Ingestion container entry point, in pipeline order —
  # see ecs_task.tf/state_machine.tf.
  ingestion_steps = ["extract-stage", "promote", "graph-load-start", "graph-load-poll"]

  # Falls back to this module's own ECR repo at "latest" when unset.
  resolved_ingestion_image = var.ingestion_container_image != "" ? var.ingestion_container_image : "${aws_ecr_repository.ecr_ingestion.repository_url}:latest"

  # Common ecs:runTask.sync2 Task-state shape, one per THOR_STEP — Next/{extract-stage,promote,...}
  # is merged in per-state below since it differs per state. Guarded by ingestion_active so this
  # never indexes the (empty when inactive) for_each task-definition map.
  ingestion_task_states = local.ingestion_active ? {
    for step in local.ingestion_steps : step => {
      Type     = "Task"
      Resource = "arn:aws:states:::ecs:runTask.sync2"
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
      Retry = [
        {
          ErrorEquals     = ["States.ALL"]
          IntervalSeconds = 30
          MaxAttempts     = var.max_retry_count
          BackoffRate     = 2
        }
      ]
      Catch = [
        {
          ErrorEquals = ["States.ALL"]
          Next        = "SendToDeadLetterQueue"
        }
      ]
    }
  } : {}
}
