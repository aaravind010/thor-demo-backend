variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod)"
}

variable "vpc_id" {
  type        = string
  description = "VPC to deploy into"
}

variable "vpc_cidr" {
  type        = string
  description = "CIDR of the VPC — scopes the security group's egress to VPC-only, same convention as the ecs module"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "Private subnets for the DB subnet group (needs at least 2, in different AZs)"
}

variable "task_api_security_group_id" {
  type        = string
  default     = null
  description = "task-api's ECS service security group ID — null until enable_compute is true (task-api's SG doesn't exist yet). Only referenced inside the ingress rule's body, never used to decide whether that rule exists (see create_task_api_ingress) — this value can be \"known after apply\" without breaking anything, since count/for_each are the only place Terraform requires a plan-time-known value."
}

variable "create_task_api_ingress" {
  type        = bool
  default     = false
  description = "Whether to create the ingress rule granting task-api access to the database — pass var.enable_compute here, not a `!= null` check on task_api_security_group_id. A count/for_each meta-argument must be fully known at plan time; task_api_security_group_id's *value* comes from a security group Terraform may still be creating/replacing in this same apply, so gating count on a null-check of it fails with \"Invalid count argument\" the moment that security group actually changes. enable_compute itself is a plain, statically-known bool, so gating on that instead sidesteps the problem entirely — the possibly-still-unknown ID only gets consumed inside the resource body below, which is fine."
}

variable "database_name" {
  type        = string
  default     = ""
  description = "Initial database name created on the cluster. \"\" falls back to \"thor_<environment>_db\" — same empty-string-means-computed-default idiom already used for container_image elsewhere in this repo."
}

variable "master_username" {
  type        = string
  default     = "thor_admin"
  description = "Master username — the password itself is never set here; manage_master_user_password lets AWS generate and rotate it in Secrets Manager instead"
}

variable "engine_version" {
  type        = string
  default     = "16.13"
  description = "Aurora PostgreSQL engine version — must be >= 16.1 for RDS Data API support. Confirm against `aws rds describe-db-engine-versions` before relying on this default; AWS periodically retires old patch versions."
}

variable "min_capacity" {
  type        = number
  default     = 0.5
  description = "Serverless v2 minimum ACU (Aurora Capacity Unit)"
}

variable "max_capacity" {
  type        = number
  default     = 1
  description = "Serverless v2 maximum ACU"
}

variable "backup_retention_days" {
  type        = number
  default     = 7
  description = "Automated backup retention period"
}

variable "deletion_protection" {
  type        = bool
  default     = false
  description = "Prevent accidental deletion — should be true for prod, false for throwaway dev/qa environments"
}

variable "skip_final_snapshot" {
  type        = bool
  default     = true
  description = "Skip taking a final snapshot on destroy — should be false for prod, true for throwaway dev/qa environments"
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Additional resource-specific tags"
}
