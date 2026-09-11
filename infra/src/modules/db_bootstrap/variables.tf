variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod)"
}

variable "vpc_id" {
  type        = string
  description = "VPC the bootstrap Lambda's security group lives in"
}

variable "vpc_cidr" {
  type        = string
  description = "VPC CIDR — scopes the bootstrap security group's egress"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "Private subnets the in-VPC bootstrap Lambda attaches ENIs into"
}

variable "aurora_security_group_id" {
  type        = string
  description = "Aurora security group ID — the module adds an ingress rule to it (out-of-module, to avoid a dependency cycle) so the bootstrap can reach the writer on 5432"
}

variable "aurora_writer_endpoint" {
  type        = string
  description = "Aurora writer endpoint the bootstrap connects to directly for DDL (roles/grants) — THOR_BOOTSTRAP_HOST"
}

variable "aurora_database_name" {
  type        = string
  description = "Master DB name to connect to (table/schema grants are per-database) — THOR_BOOTSTRAP_DATABASE"
}

variable "master_user_secret_arn" {
  type        = string
  description = "ARN of the AWS-managed Aurora master credential secret the bootstrap reads to authenticate as master"
}

variable "source_dir" {
  type        = string
  description = "Absolute path to the published Thor.DbBootstrap Lambda artifact"
}

variable "runtime" {
  type        = string
  description = "Lambda runtime — must match the handler format if changed"
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
