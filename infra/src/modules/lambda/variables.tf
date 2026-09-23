variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod)"
}

variable "aurora_database_name" {
  type        = string
  description = "Master DB name the authorizer queries (subdomain -> tenant routing) — THOR_MASTERDB_DATABASE"
}

variable "rds_proxy_resource_id" {
  type        = string
  description = "RDS Proxy resource ID (prx-...) — the middle segment of the rds-db:connect ARN scoping the authorizer to its thor_authorizer db-user. The proxy, not the cluster: that is what the authorizer connects to."
}

variable "authorizer_db_user" {
  type        = string
  description = "Least-privilege read-only Postgres role (rds_iam, no password) the authorizer assumes via IAM to read the Master DB. Bootstrapped by the db_bootstrap Lambda."
  default     = "thor_authorizer"
}

variable "rds_proxy_endpoint" {
  type        = string
  description = "RDS Proxy endpoint the authorizer connects through — THOR_MASTERDB_HOST"
}

variable "rds_proxy_security_group_id" {
  type        = string
  description = "RDS Proxy security group ID — the authorizer module adds an ingress rule to it (out-of-module, to avoid a dependency cycle) so the authorizer can reach the proxy on 5432"
}

variable "vpc_id" {
  type        = string
  description = "VPC the authorizer Lambda's security group lives in"
}

variable "vpc_cidr" {
  type        = string
  description = "VPC CIDR — scopes the authorizer security group's egress"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "Private subnets the in-VPC authorizer Lambda attaches ENIs into"
}

variable "authorizer_salt_secret_arn" {
  type        = string
  description = "ARN of the Secrets Manager secret holding the shared PBKDF2 salt (module.secrets) — passed through as THOR_AUTHORIZER_SALT_SECRET_ID"
}

variable "connector_jwt_public_key" {
  type        = string
  description = "PEM public half of the connector JWT keypair, passed through as THOR_TASKAPI_JWT_PUBLIC_KEY. A literal rather than a Secrets Manager reference because Lambda env vars have no valueFrom indirection — see the root module's variable of the same name."
}

variable "iam_permissions_boundary_arn" {
  type        = string
  description = "ARN of the Console-created thor-<environment>-role-boundary policy"
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags"
  default     = {}
}

variable "runtime" {
  type        = string
  description = "Lambda runtime — must match the handler format (Assembly::Namespace.Class::Method) if changed"
  default     = "dotnet10"
}

variable "timeout" {
  type        = number
  description = "Function timeout in seconds"
  default     = 5
}

variable "memory_size" {
  type        = number
  description = "Function memory in MB"
  default     = 256
}

variable "authorizer_source_dir" {
  type        = string
  description = "Absolute path to lambda authorizer code"
}