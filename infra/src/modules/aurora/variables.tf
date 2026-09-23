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

variable "rds_proxy_security_group_id" {
  type        = string
  description = "RDS Proxy's security group ID — RDS Proxy is mandatory, so this is always provided."
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
  description = "Aurora PostgreSQL engine version. Confirm against `aws rds describe-db-engine-versions` before relying on this default; AWS periodically retires old patch versions."
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

variable "iam_database_authentication_enabled" {
  type        = bool
  default     = true
  description = "Enable RDS IAM database authentication on the cluster. Required for the IAM-only auth model (runtime via RDS Proxy, provisioning Lambdas direct) — there are no DB passwords."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Additional resource-specific tags"
}
