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
  description = "ARN of the account's Console-created thor-<environment>-role-boundary policy — required on this module's IAM roles' permissions_boundary argument, or role creation is rejected."
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags"
  default     = {}
}

variable "max_retry_count" {
  type        = number
  description = "MaxAttempts for each ingestion step's own Step Functions Retry policy — retries just the failed step in place (every step is independently idempotent) before escalating to the DLQ. Not related to SQS's native redrive (see sqs_max_receive_count), which only covers Pipe delivery failures."
  default     = 3
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
  description = "How many SQS messages the Pipe delivers to CreateManifest per invocation. Kept at 1 by default to keep CreateManifest's per-file logic simple."
  default     = 1
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

variable "ingestion_container_image" {
  type        = string
  description = "Pinned image URI for the ingestion ECS task; \"\" (default) falls back to this module's own ECR repo at :latest."
  default     = ""
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
