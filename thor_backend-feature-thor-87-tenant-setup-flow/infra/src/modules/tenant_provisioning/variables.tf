variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod)"
}

variable "vpc_id" {
  type        = string
  description = "VPC the provisioning Lambdas run in (they reach Aurora over TCP, so they need VPC placement)"
}

variable "vpc_cidr" {
  type        = string
  description = "CIDR of the VPC — scopes the provisioning security group's egress to VPC-only, same convention as the aurora/ecs modules"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "Private subnets the Lambdas attach to (needs at least 2, in different AZs)"
}

variable "aurora_security_group_id" {
  type        = string
  description = "Aurora's security group — this module adds an ingress rule allowing the provisioning SG to reach it on 5432 (DDL + master writes go direct to the writer, not through a proxy)"
}

variable "aurora_writer_endpoint" {
  type        = string
  description = "Aurora writer endpoint — the DDL/master Lambdas connect here directly with an IAM token"
}

variable "aurora_database_name" {
  type        = string
  description = "Master metadata database name on the cluster"
}

variable "aurora_cluster_resource_id" {
  type        = string
  description = "Aurora cluster resource ID — scopes the DDL Lambda's rds-db:connect ARN to the provisioning DB user"
}

variable "tenant_routing_endpoint" {
  type        = string
  description = "Endpoint written into every tenant's routing row (cluster_endpoint) — the IAM RDS Proxy the runtime connects through"
}

variable "provisioning_db_user" {
  type        = string
  default     = "thor_provisioner"
  description = "DDL role (rds_iam, CREATEDB/CREATEROLE) the create-tenant-database Lambda assumes via IAM"
}

variable "metadata_writer_db_user" {
  type        = string
  default     = "thor_metadata_writer"
  description = "Metadata-write role (rds_iam, DML on the auth tables, no DDL power) the seed + finalize-routing Lambdas assume via IAM"
}

variable "source_dir" {
  type        = string
  description = "Absolute path to the published Thor.TenantProvisioning artifact (…/backend/functions/Thor.TenantProvisioning/publish). One zip serves all six Lambdas; the statemachine ASL is resolved relative to it."
}

variable "hosted_zone_id" {
  type        = string
  description = "Route53 hosted zone ID for tenant subdomains"
}

variable "base_domain" {
  type        = string
  description = "Base domain under which tenant subdomains are created"
}

variable "dns_target" {
  type        = string
  description = "DNS target each tenant subdomain CNAME points at"
}

variable "runtime" {
  type        = string
  default     = "dotnet10"
  description = "Lambda runtime for the provisioning functions"
}

variable "timeout" {
  type        = number
  default     = 60
  description = "Lambda timeout in seconds — the DDL step creates a database and roles, so it needs more than the default"
}

variable "memory_size" {
  type        = number
  default     = 512
  description = "Lambda memory in MB"
}

variable "alarm_actions" {
  type        = list(string)
  default     = []
  description = "Optional SNS topic ARNs notified when an execution fails. Empty = the alarm still surfaces in CloudWatch, just sends no notification."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Additional resource-specific tags"
}
