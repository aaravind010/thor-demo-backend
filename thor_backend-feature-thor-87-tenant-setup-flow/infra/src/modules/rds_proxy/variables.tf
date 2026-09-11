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
  description = "CIDR of the VPC — scopes the security group's egress to VPC-only, same convention as the aurora/ecs modules"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "Private subnets for the proxy (needs at least 2, in different AZs)"
}

variable "aurora_cluster_identifier" {
  type        = string
  description = "Aurora cluster identifier the proxy targets"
}

variable "aurora_cluster_resource_id" {
  type        = string
  description = "Aurora cluster resource ID — used to scope the proxy role's rds-db:connect ARN. End-to-end IAM means no master secret is involved."
}

variable "allowed_security_group_ids" {
  type        = map(string)
  default     = {}
  description = "Security group IDs allowed to reach the proxy on 5432, one ingress rule per entry — keyed by a static consumer name (e.g. \"thor\", \"task-api\"), same reasoning as the aurora module's own variable of this name."
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags"
  default     = {}
}
