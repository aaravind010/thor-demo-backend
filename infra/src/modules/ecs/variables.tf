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
  description = "CIDR of the VPC — scopes each service security group's egress to VPC-only (no NAT/IGW)"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "Private subnets for the ECS tasks"
}

variable "enable_container_insights" {
  type        = bool
  description = "Enable ECS Container Insights on the cluster"
  default     = true
}

variable "iam_permissions_boundary_arn" {
  type        = string
  description = "ARN of the Console-created thor-<environment>-role-boundary policy"
}

variable "enable_compute" {
  type        = bool
  description = "Whether to create the ECS services (task defs, ECS services, NLB/target groups, per-service IAM/SG) for local.active_services. The cluster, Service Connect namespace, and ECR repos (ecr.tf) are created regardless — set false to bring up an environment's cluster/registry only, before real images exist."
  default     = true
}

variable "services" {
  description = "Per-service configuration, keyed by service name (thor-api, task-api, intelligence-engine). expose_via_nlb=true gets a private NLB (nlb.tf, thor-api only) reached via VPC Link from API Gateway, not directly from the internet; services with expose_via_nlb=false accept traffic only from the public services' security groups, reachable internally via Service Connect using the map key as the client_alias dns_name. deployment_strategy=BLUE_GREEN is a per-deploy toggle (deployment_strategy_plan.md reserves it for DB-schema-change deploys) — for a publicly-exposed service it shifts the NLB's production listener between blue/green target groups; for an internal service it's a plain task-set swap."
  type = map(object({
    container_image        = string
    container_port         = optional(number, 8080)
    cpu                    = optional(number, 512)
    memory                 = optional(number, 1024)
    desired_count          = optional(number, 2)
    min_healthy_percent    = optional(number, 100)
    max_percent            = optional(number, 200)
    health_check_path      = optional(string, "/health")
    log_retention_days     = optional(number, 30)
    environment_variables  = optional(map(string), {})
    secrets                = optional(map(string), {})
    expose_via_nlb         = optional(bool, false)
    nlb_listener_port      = optional(number, 80)
    nlb_test_listener_port = optional(number, 8081)
    deployment_strategy    = optional(string, "ROLLING")
    bake_time_in_minutes   = optional(number, 5)
  }))

  validation {
    condition     = alltrue([for k, v in var.services : contains(["ROLLING", "BLUE_GREEN"], v.deployment_strategy)])
    error_message = "deployment_strategy must be either \"ROLLING\" or \"BLUE_GREEN\" for every service."
  }

  validation {
    condition     = alltrue([for k, v in var.services : v.bake_time_in_minutes >= 0 && v.bake_time_in_minutes <= 1440])
    error_message = "bake_time_in_minutes must be between 0 and 1440 (24 hours) for every service."
  }
}

variable "nlb_certificate_arn" {
  type        = string
  description = "ACM certificate for the NLB's TLS listener (NLB <-> ECS re-encryption). \"\" (default) keeps the NLB on plain TCP and the publicly-exposed service's container healthcheck on plain HTTP — today's behavior. Set only where the re-encryption path is actually wanted."
  default     = ""
}

variable "cross_account_pull_principal_arns" {
  type        = list(string)
  description = "IAM role ARNs (e.g. a downstream environment's deploy role, possibly in another AWS account) allowed to pull images from this environment's ECR repos. Empty by default — fill in once the downstream role actually exists (e.g. qa's terragrunt.hcl sets this to prod's deploy role ARN once the prod account/role are real)."
  default     = []
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags"
  default     = {}
}
