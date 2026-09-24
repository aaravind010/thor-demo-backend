variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod)"
}

variable "account_id" {
  type        = string
  description = "AWS account ID — used for the bucket's globally-unique name and hand-built ARNs."
}

variable "aws_region" {
  type        = string
  description = "AWS region — RDS IAM token region and the awslogs-region log driver option."
}

variable "vpc_id" {
  type        = string
  description = "VPC the runner task's security group is created in"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "Private subnets the runner task runs in"
}

variable "iam_permissions_boundary_arn" {
  type        = string
  description = "ARN of the account's thor-<environment>-role-boundary policy — required on every IAM role this module creates."
}

variable "github_oidc_role_arn" {
  type        = string
  description = "ARN of the GitHub OIDC deploy role (deploy-<environment>, created by scripts/create-deploy-role.sh, not Terraform). One of only three principals the migration bucket policy allows — a wrong value locks CI (and Terraform running as that role) out of the bucket."
}

variable "db_host" {
  type        = string
  description = "RDS Proxy endpoint — where the runner reads the master DB's tenant routing."
}

variable "db_name" {
  type        = string
  description = "Master DB name"
}

variable "rds_proxy_resource_id" {
  type        = string
  description = "RDS Proxy resource ID (prx-...) that rds-db:connect is scoped to."
}

variable "master_db_app_user" {
  type        = string
  description = "Master DB read role for the tenant list (thor_app)."
}

variable "state_machine_definition_path" {
  type        = string
  description = "Absolute path to migrations/tenant/statemachine/tenant-migration.asl.json."
}

variable "task_cpu" {
  type        = number
  description = "Fargate CPU units for the runner task"
  default     = 512
}

variable "task_memory" {
  type        = number
  description = "Fargate memory (MiB) for the runner task"
  default     = 1024
}

variable "approval_timeout_seconds" {
  type        = number
  description = "How long WaitForPlanAndApproval waits for CI (plan + reviewer) before the execution fails."
  default     = 86400
}

variable "apply_max_concurrency" {
  type        = number
  description = "Tenants the Apply Map migrates in parallel."
  default     = 5
}

variable "run_retention_days" {
  type        = number
  description = "Days runs/<runId>/ objects (plans, summaries, tokens) are kept. state/ never expires."
  default     = 90
}

variable "log_retention_days" {
  type        = number
  description = "CloudWatch log retention for the runner task and the state machine"
  default     = 30
}

variable "enable_container_insights" {
  type        = bool
  description = "Enable ECS Container Insights on the runner cluster"
  default     = false
}

variable "alarm_actions" {
  type        = list(string)
  default     = []
  description = "Optional SNS topic ARNs notified when an execution fails. Empty = the alarm still surfaces in CloudWatch, just sends no notification."
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags"
  default     = {}
}
