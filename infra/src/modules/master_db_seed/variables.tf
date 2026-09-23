variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod)"
}

variable "vpc_id" {
  type        = string
  description = "VPC the seed Lambda's security group lives in"
}

variable "vpc_cidr" {
  type        = string
  description = "VPC CIDR — scopes the seed security group's egress"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "Private subnets the in-VPC seed Lambda attaches ENIs into"
}

variable "aurora_database_name" {
  type        = string
  description = "Master DB name the seed Lambda writes into — THOR_MASTERDB_DATABASE"
}

variable "rds_proxy_resource_id" {
  type        = string
  description = "RDS Proxy resource ID (prx-...) — the middle segment of the rds-db:connect ARN scoping the seed Lambda to its thor_master_seed db-user. The proxy, not the cluster: that is what the seed Lambda connects to."
}

variable "rds_proxy_endpoint" {
  type        = string
  description = "RDS Proxy endpoint the seed Lambda connects through — THOR_MASTERDB_HOST"
}

variable "rds_proxy_security_group_id" {
  type        = string
  description = "RDS Proxy security group ID — this module adds an ingress rule to it (out-of-module, to avoid a dependency cycle) so the seed Lambda can reach the proxy on 5432"
}

variable "master_seed_db_user" {
  type        = string
  default     = "thor_master_seed"
  description = "Least-privilege Postgres role (rds_iam, no password) the seed Lambda assumes via IAM to write deployment seed data into the Master DB. Bootstrapped by the db_bootstrap Lambda's db-roles.sql."
}

variable "source_dir" {
  type        = string
  description = "Absolute path to the published Thor.MasterDbSeed Lambda artifact"
}

variable "iam_permissions_boundary_arn" {
  type        = string
  description = "ARN of the Console-created thor-<environment>-role-boundary policy"
}

variable "runtime" {
  type        = string
  description = "Lambda runtime — must match the handler format (Assembly::Namespace.Class::Method) if changed"
  default     = "dotnet10"
}

variable "timeout" {
  type        = number
  description = "Function timeout in seconds"
  default     = 60
}

variable "memory_size" {
  type        = number
  description = "Function memory in MB"
  default     = 256
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags"
  default     = {}
}
