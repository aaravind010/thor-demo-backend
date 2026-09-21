variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod)"
}

variable "account_id" {
  type        = string
  description = "AWS account ID for this environment — used to build the ingestion bucket's globally-unique name."
}

variable "aws_region" {
  type        = string
  description = "AWS region for this environment — used for the ingestion ECS task's awslogs-region log driver option."
}

variable "enable_ingestion" {
  type        = bool
  description = "Whether to create the ingestion pipeline. The ECR repo is created regardless, so an image can be pushed before enabling."
}

variable "vpc_id" {
  type        = string
  description = "VPC the ingestion ECS task's security group is created in"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "Private subnets the ingestion ECS task runs in"
}

variable "enable_container_insights" {
  type        = bool
  description = "Enable ECS Container Insights on this module's own dedicated ingestion cluster"
}

variable "iam_permissions_boundary_arn" {
  type        = string
  description = "ARN of the account's Console-created thor-<environment>-role-boundary policy — required on this module's IAM roles' permissions_boundary argument, or role creation is rejected. Effective permissions are the intersection with it, so beyond the ECS-era actions it must also allow ec2:DescribeSubnets (the in-VPC Lambda functions fail to Active without it), states:DescribeExecution + states:StopExecution (the Distributed Map's child executions), and s3:PutObject on the map-results bucket (ResultWriter)."
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags"
  default     = {}
}

variable "max_retry_count" {
  type        = number
  description = "MaxAttempts of the Step Functions Retry policy every state gets (both compute targets, Map items, DriverInvoke, SendToDeadLetterQueue) — retries just the failed state in place (every step is independently idempotent) before its Catch escalates to the DLQ. Counts retries after the first attempt, so 3 = up to 4 executions. The ECS graph-load-poll step uses graph_load_poll_max_attempts instead. Not related to SQS's native redrive (see sqs_max_receive_count), which only covers Pipe delivery failures."
  default     = 3
}

variable "retry_interval_seconds" {
  type        = number
  description = "Seconds before the first retry of a failed state; subsequent waits are multiplied by retry_backoff_rate."
  default     = 30
}

variable "retry_backoff_rate" {
  type        = number
  description = "Multiplier applied to retry_interval_seconds on each successive retry — 2 gives 30 s, 60 s, 120 s (210 s total before the DLQ with max_retry_count = 3), letting a throttled dependency recover; 1 keeps a fixed interval."
  default     = 2
}

variable "graph_load_poll_interval_seconds" {
  type        = number
  description = "Seconds between graph-load-poll invocations while the Neptune bulk load is still running — the Lambda branch's Wait state, and the ECS branch's Retry interval."
  default     = 30
}

variable "graph_load_poll_max_attempts" {
  type        = number
  description = "ECS branch only: how many times graph-load-poll is re-run (every graph_load_poll_interval_seconds) before the load is treated as failed. ecs:runTask.sync can't distinguish 'still in progress' from failure, so this bounded Retry stands in for a real poll loop — size it to the longest expected bulk load."
  default     = 20
}

variable "map_max_concurrency" {
  type        = number
  description = "MaxConcurrency of extract-stage's per-file Distributed Map. Tune against per-tenant RDS Proxy connection limits (ADR §9's noisy-neighbor concern)."
  default     = 10
}

variable "map_results_retention_days" {
  type        = number
  description = "S3 lifecycle expiry for the Distributed Map's ResultWriter output — nothing reads these objects back, they're for post-hoc inspection only."
  default     = 30
}

variable "sqs_visibility_timeout_seconds" {
  type        = number
  description = "How long a message is hidden once the Pipe picks it up — must exceed create_manifest_timeout, since the Pipe holds the message for the full synchronous invocation."
  default     = 60
}

variable "sqs_max_receive_count" {
  type        = number
  description = "SQS's native redrive threshold to the DLQ — kept above max_retry_count so it only fires on repeated Pipe delivery failures, a separate failure class from the state machine's own retry loop."
  default     = 5
}

variable "pipe_batch_size" {
  type        = number
  description = "How many SQS messages the Pipe delivers to CreateManifest per invocation. CreateManifest loops over the whole batch, starting one Step Functions execution per message."
  default     = 10
}

variable "log_retention_days" {
  type        = number
  description = "CloudWatch Logs retention for this module's log groups (CreateManifest, the ingestion ECS task, the state machine)"
  default     = 30
}

variable "create_manifest_source_dir" {
  type        = string
  description = "Absolute path to CreateManifest's dotnet publish output — must be absolute since Terragrunt only copies infra/src. data.archive_file zips it; dotnet publish must already have written here."
}

variable "create_manifest_timeout" {
  type        = number
  description = "CreateManifest Lambda timeout in seconds — must stay under sqs_visibility_timeout_seconds."
  default     = 30
}

variable "create_manifest_memory_size" {
  type        = number
  description = "CreateManifest Lambda memory in MB"
  default     = 256
}

variable "ingestion_driver_source_dir" {
  type        = string
  description = "Absolute path to Thor.Workflows.IngestionDriver's dotnet publish output — same contract as create_manifest_source_dir."
}

variable "ingestion_driver_timeout" {
  type        = number
  description = "IngestionDriver Lambda timeout in seconds — it lists the manifest's files and HEADs each one in S3."
  default     = 60
}

variable "ingestion_driver_memory_size" {
  type        = number
  description = "IngestionDriver Lambda memory in MB"
  default     = 512
}

variable "ingestion_driver_max_bytes" {
  type        = number
  description = "THOR_INGESTION_DRIVER_MAX_BYTES: manifests whose files total at most this many bytes run on the Lambda branch; larger ones on ECS (ComputeTargetSelector.cs)."
  default     = 5000000 # 5 MB
}

variable "graph_load_start_stale_seconds" {
  type        = number
  description = "THOR_GRAPH_BULKLOAD_START_STALE_SECONDS: how long a GraphBulkLoadJob may sit in 'starting' before graph-load-start treats the reservation as abandoned and takes it over (GraphLoadStartStep.cs)."
  default     = 300
}

variable "ingestion_container_image" {
  type        = string
  description = "Pinned image URI for the ingestion ECS task definitions and per-step Lambda functions; \"\" (default) falls back to this module's own ECR repo at :latest. Unlike an ECS task definition, Lambda validates the image at CreateFunction time — so the image must already be in ECR (built for linux/amd64) before enable_ingestion can apply. The repo is tag-immutable: :latest can be pushed exactly once; every later build needs a fresh tag set here."
  default     = ""
}

variable "ingestion_lambda_timeout" {
  type        = number
  description = "Timeout in seconds for each per-step ingestion Lambda function. Lambda's hard ceiling is 900; manifests too large to fit go to the ECS branch by way of ingestion_driver_max_bytes."
  default     = 900
}

variable "ingestion_lambda_memory_size" {
  type        = number
  description = "Memory in MB for each per-step ingestion Lambda function"
  default     = 2048
}

variable "ingestion_task_cpu" {
  type        = number
  description = "Fargate CPU units for the ingestion task definition"
  default     = 512
}

variable "ingestion_task_memory" {
  type        = number
  description = "Fargate memory (MB) for the ingestion task definition"
  default     = 1024
}

variable "aurora_secret_arn" {
  type        = string
  description = "Aurora master-user Secrets Manager ARN — the ingestion execution role fetches it to inject THOR_MASTERDB_USER/THOR_MASTERDB_PASSWORD into every task def's container (same secrets-block idiom as thor-api/task-api); the Lambda functions get it as THOR_MASTERDB_SECRET_ARN to resolve themselves. module.aurora is unconditional, so this is always a real value."
}

variable "db_host" {
  type        = string
  description = "What the ingestion container should connect to for Postgres — module.rds_proxy.endpoint, not Aurora's own endpoint, same as thor-api/task-api"
}

variable "db_name" {
  type        = string
  description = "Aurora database name — module.aurora.database_name"
}

variable "neptune_endpoint" {
  type        = string
  default     = ""
  description = "Neptune cluster writer endpoint, injected into every task def's container and Lambda function as THOR_NEPTUNE_ENDPOINT. \"\" when enable_neptune is off — module.neptune is count-gated, may not exist."
}

variable "neptune_cluster_resource_id" {
  type        = string
  default     = ""
  description = "Neptune's cluster_resource_id (not the cluster identifier) — needed to build the neptune-db:* IAM policy ARN in ecs_task.tf. \"\" means Neptune is off, in which case that IAM statement is skipped entirely rather than built against an invalid empty-resource-id ARN."
}

variable "neptune_loader_role_arn" {
  type        = string
  default     = ""
  description = "ARN of the IAM role attached to the Neptune cluster for bulk loads (module.neptune's bulk_load_role_arn) — GraphLoadStartStep hands it to the loader as THOR_GRAPH_BULKLOAD_IAM_ROLE_ARN so the cluster can read the CSVs this module's graph-load steps write to the ingestion bucket. \"\" when enable_neptune is off."
}
