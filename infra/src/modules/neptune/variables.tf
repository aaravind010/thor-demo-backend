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
  description = "CIDR of the VPC — scopes the security group's egress to VPC-only, same convention as modules/aurora and modules/ecs"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "Private subnets for the Neptune subnet group (needs at least 2, in different AZs)"
}

variable "consumer_security_group_ids" {
  type        = map(string)
  default     = {}
  description = "Security group IDs allowed to reach Neptune on 8182 (Gremlin), one ingress rule per entry — keyed by a static consumer name (e.g. \"intelligence-engine\", \"task-api\"), not the ID itself, same for_each-needs-static-keys reasoning as modules/aurora's allowed_security_group_ids. Only used when create_ingress is true."
}

variable "create_ingress" {
  type        = bool
  description = "Whether to create the consumer_security_group_ids ingress rules — wired to enable_compute at the root, not inferred from consumer_security_group_ids being non-empty (an empty map is ambiguous about why it's empty; this flag isn't)."
}

variable "engine_version" {
  type        = string
  default     = "1.4.8.0"
  description = "Neptune engine version — must be >= 1.3.0.0 for Serverless v2 support. Confirm against AWS's engine-releases page before relying on this default long-term; Neptune retires old versions on a rolling schedule."
}

variable "min_capacity" {
  type        = number
  default     = 1
  description = "Serverless v2 minimum NCU (Neptune Capacity Unit)"
}

variable "max_capacity" {
  type        = number
  default     = 2
  description = "Serverless v2 maximum NCU"
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
