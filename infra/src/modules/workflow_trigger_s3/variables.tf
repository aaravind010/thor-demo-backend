variable "name" {
  description = "Workflow this trigger starts, e.g. \"ingestion\"."
  type        = string
}

variable "enabled" {
  description = "Create the trigger. False leaves the uploads bucket without a notification, so nothing starts executions."
  type        = bool
  default     = false
}

# --- the bucket is an input, never created here ------------------------------------------------

variable "bucket_id" {
  description = "Uploads bucket to attach the notification to. Owned by modules/uploads and already in use — this module must never declare it. modules/ingestion did, with the same computed name, which would have failed the first enable_ingestion apply with BucketAlreadyOwnedByYou."
  type        = string
}

variable "bucket_arn" {
  description = "ARN of the same bucket, for the queue policy's aws:SourceArn condition."
  type        = string
}

variable "filter_prefix" {
  description = <<-EOT
    Key prefix the notification fires on. Required, and deliberately has no default: an empty prefix
    means every write to the bucket starts a workflow, which is how the old module fed its own
    Neptune bulk-load output back into the pipeline.

    S3 notification filters take a literal prefix with no wildcards, so a per-tenant path cannot be
    expressed. For ingestion this is "tenants/", matching what Thor.TaskApi presigns
    (tenants/{tenantId}/uploads/{scanId}/{sourceId}/{file}) and not matching anything the pipeline
    writes.
  EOT
  type        = string

  validation {
    condition     = var.filter_prefix != ""
    error_message = "filter_prefix must not be empty — an unfiltered notification fires on the pipeline's own writes."
  }
}

# --- wiring to modules/workflow ----------------------------------------------------------------

variable "queue_name" {
  description = "Name for the trigger queue. Comes from modules/workflow's trigger_queue_name output, which its DLQ redrive_allow_policy already names by built ARN — so this module depends on the workflow and nothing depends back."
  type        = string
}

variable "dlq_arn" {
  description = "The workflow's dead-letter queue, from modules/workflow."
  type        = string

  # modules/workflow returns "" for a workflow whose definition names no $${dlq_url}. That is fine for
  # a workflow nothing triggers, but this module always sets a redrive_policy, and a trigger queue
  # whose redrive target is empty is a silent message sink. A workflow with an S3 trigger has to
  # declare a DLQ in its definition.
  validation {
    condition     = var.dlq_arn != ""
    error_message = "A triggered workflow needs a dead-letter queue: its definition must name $${dlq_url} so modules/workflow creates one."
  }
}

variable "state_machine_arn" {
  description = "The workflow's state machine. The handler is granted states:StartExecution on exactly this."
  type        = string
}

# --- the handler -------------------------------------------------------------------------------

variable "handler_source_dir" {
  description = "Absolute path to the handler's dotnet publish output. Terraform zips it; it does not compile (scripts/publish-lambda-functions.sh does). Absolute because Terragrunt copies only infra/src, not backend/."
  type        = string
}

variable "handler_entrypoint" {
  description = "Lambda handler string, assembly::namespace.class::method."
  type        = string
}

variable "handler_runtime" {
  description = "Lambda runtime for the handler."
  type        = string
  default     = "dotnet10"
}

variable "handler_timeout" {
  description = "Handler timeout in seconds."
  type        = number
  default     = 60
}

variable "handler_memory_size" {
  description = "Handler memory (MiB)."
  type        = number
  default     = 512
}

variable "handler_environment" {
  description = "Handler environment variables. The state machine ARN is merged in under state_machine_env_var, so the caller need not thread a module output back through its own inputs."
  type        = map(string)
  default     = {}
}

variable "state_machine_env_var" {
  description = "Environment variable the handler reads the state machine ARN from. Configurable because the handler is existing code with its own contract — Thor.CreateManifest's CompositionRoot reads THOR_INGESTION_STATE_MACHINE_ARN — and renaming that belongs with folding the handler into the workflow image, not here."
  type        = string
  default     = "THOR_WORKFLOW_STATE_MACHINE_ARN"
}

variable "handler_policy_statements" {
  description = "Statements the handler needs beyond states:StartExecution and basic VPC execution — typically rds-db:connect for the routing lookup."
  type = list(object({
    actions   = list(string)
    resources = list(string)
  }))
  default = []
}

# --- platform ----------------------------------------------------------------------------------

variable "private_subnet_ids" {
  description = "Subnets the handler's ENIs attach to."
  type        = list(string)
}

variable "security_group_id" {
  description = "The workflow's security group, from modules/workflow_network."
  type        = string
}

variable "environment" {
  description = "Deployment environment (dev, qa, prod)."
  type        = string
}

variable "iam_permissions_boundary_arn" {
  description = "Permissions boundary applied to every role this module creates."
  type        = string
}

# --- queue / pipe tuning -----------------------------------------------------------------------

variable "sqs_visibility_timeout_seconds" {
  description = "Trigger queue visibility timeout."
  type        = number
  default     = 120
}

variable "sqs_max_receive_count" {
  description = "Backstop for repeated Pipe delivery failures before a message goes to the DLQ. Separate from the state machine's own retries, and kept above them since requeued messages never trip this counter on their own."
  type        = number
  default     = 5
}

variable "pipe_batch_size" {
  description = "Messages the Pipe delivers to the handler per invocation."
  type        = number
  default     = 10
}

variable "pipe_maximum_batching_window_seconds" {
  description = "Longest the Pipe waits to fill pipe_batch_size before delivering a partial batch."
  type        = number
  default     = 60
}

variable "log_retention_days" {
  description = "CloudWatch log retention for the handler."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Tags applied to every resource in this module."
  type        = map(string)
  default     = {}
}
