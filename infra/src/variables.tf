variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod)"
}

# --- network ---

variable "enable_network" {
  type        = bool
  description = "Whether to create this environment's own VPC. Set false to borrow dev's VPC instead (see dev_* variables) and skip that VPC's endpoint/NAT costs entirely."
  default     = true
}

variable "vpc_cidr" {
  type        = string
  description = "CIDR block for the VPC. Unused when enable_network is false."
  default     = ""
}

variable "az_count" {
  type        = number
  description = "Number of availability zones to span"
  default     = 2
}

variable "public_subnet_cidrs" {
  type        = list(string)
  description = "CIDR blocks for public subnets, one per AZ. Unused when enable_network is false."
  default     = []
}

variable "private_subnet_cidrs" {
  type        = list(string)
  description = "CIDR blocks for private subnets, one per AZ. Unused when enable_network is false."
  default     = []
}

variable "enable_vpc_endpoints" {
  type        = bool
  description = "Create gateway/interface VPC endpoints instead of NAT Gateway egress"
  default     = true
}

# --- dev's network (used only when enable_network = false) ---

variable "dev_vpc_id" {
  type        = string
  description = "dev's VPC ID, to deploy compute into when this environment doesn't create its own (passed in via a Terragrunt dependency block)"
  default     = null
}

variable "dev_vpc_cidr" {
  type        = string
  description = "CIDR of dev's VPC — scopes the compute security group's egress"
  default     = null
}

variable "dev_public_subnet_ids" {
  type        = list(string)
  description = "dev's public subnet IDs, for the ALB"
  default     = []
}

variable "dev_private_subnet_ids" {
  type        = list(string)
  description = "dev's private subnet IDs, for the ECS tasks"
  default     = []
}

# --- compute (shared ECS cluster running thor, task-api, intelligence-engine) ---

variable "enable_compute" {
  type        = bool
  description = "Whether to create the three ECS services (task defs, ECS services, NLB/target groups, per-service IAM/SG). The ECS cluster, Service Connect namespace, and ECR repos are created regardless of this flag — set false to bring up an environment's cluster/registry only, before real images exist to reference."
  default     = true
}

variable "services" {
  description = "Per-service configuration for the shared ECS cluster, keyed by service name. Must define thor-api, task-api, and intelligence-engine, with thor-api the only one setting expose_publicly = true (a private NLB reached via VPC Link from API Gateway, not the internet directly — no ALB). deployment_strategy=BLUE_GREEN is a per-deploy toggle (reserved for DB-schema-change deploys per deployment_strategy_plan.md), not a fixed per-service default."
  type = map(object({
    container_image       = optional(string, "")
    container_port        = optional(number, 8080)
    cpu                   = optional(number, 512)
    memory                = optional(number, 1024)
    desired_count         = optional(number, 2)
    min_healthy_percent   = optional(number, 100)
    max_percent           = optional(number, 200)
    health_check_path     = optional(string, "/health")
    log_retention_days    = optional(number, 30)
    environment_variables = optional(map(string), {})
    secrets               = optional(map(string), {})
    expose_publicly       = optional(bool, false)
    nlb_listener_port     = optional(number, 80)
    deployment_strategy   = optional(string, "ROLLING")
    bake_time_in_minutes  = optional(number, 5)
  }))

  default = {
    thor-api            = { container_image = "", expose_publicly = true }
    task-api            = { container_image = "" }
    intelligence-engine = { container_image = "" }
  }

  validation {
    condition     = alltrue([for k in ["thor-api", "task-api", "intelligence-engine"] : contains(keys(var.services), k)])
    error_message = "var.services must define an entry for each of: thor-api, task-api, intelligence-engine."
  }

  validation {
    condition     = contains(keys(var.services), "thor-api") ? var.services["thor-api"].expose_publicly == true : true
    error_message = "var.services.thor-api.expose_publicly must be true — thor-api is the only internet-facing service; task-api and intelligence-engine must stay internal."
  }

  validation {
    condition     = alltrue([for k, v in var.services : contains(["ROLLING", "BLUE_GREEN"], v.deployment_strategy)])
    error_message = "deployment_strategy must be either \"ROLLING\" or \"BLUE_GREEN\" for every service."
  }
}

variable "enable_container_insights" {
  type        = bool
  description = "Enable ECS Container Insights on the cluster"
  default     = true
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags"
  default     = {}
}

# --- route53 (module.route53) ---
# Off by default until domain names are confirmed and any delegation they need (the registrar's
# NS record for the apex) is actually in place.

variable "enable_route53" {
  type        = bool
  description = "Whether to create this env's hosted zones. Leave false until domain names are confirmed and delegated."
  default     = false
}

variable "hosted_zones" {
  description = "Hosted zones to manage, keyed by an arbitrary logical name (not the domain itself — that's zone_name). parent_zone_name, if set to another zone's zone_name present in this same map, gets this zone's NS delegation record created automatically in that parent (modules/route53) instead of needing to be pasted in by hand. Flattened into modules/route53's zone-name-keyed shape in main.tf. Unused while enable_route53 is false."
  type = map(object({
    zone_name        = string
    create_zone      = optional(bool, true)
    comment          = optional(string, "")
    tags             = optional(map(string), {})
    parent_zone_name = optional(string, "")
  }))
  default = {}
}

# --- frontend (static SPA: S3 + CloudFront) ---

variable "enable_frontend" {
  type        = bool
  description = "Whether to create the frontend S3 bucket + CloudFront distribution for thor-demo-frontend"
  default     = true
}

variable "frontend_price_class" {
  type        = string
  description = "CloudFront price class for the frontend distribution"
  default     = "PriceClass_100"
}

# --- api gateway authorizer (Lambda, validates connector API keys against Aurora) ---

variable "authorizer_lambda_runtime" {
  type        = string
  description = "Authorizer Lambda runtime — must match the handler format if changed"
  default     = "dotnet10"
}

variable "authorizer_lambda_timeout" {
  type        = number
  description = "Authorizer Lambda timeout in seconds"
  default     = 5
}

variable "authorizer_lambda_memory_size" {
  type        = number
  description = "Authorizer Lambda memory in MB"
  default     = 256
}

variable "authorizer_source_dir" {
  type        = string
  description = "Absolute path to lambda authorizer code"
}

variable "authorizer_db_user" {
  type        = string
  default     = "thor_authorizer"
  description = "Read-only Postgres role (rds_iam, no password) the authorizer assumes via IAM to read the Master metadata DB through the RDS Proxy. Bootstrapped by the db_bootstrap Lambda."
}

# --- db bootstrap (in-VPC Lambda that mints the platform DB roles as master) ---

variable "db_bootstrap_source_dir" {
  type        = string
  description = "Absolute path to the published Thor.DbBootstrap Lambda artifact (…/backend/functions/Thor.DbBootstrap/publish)."
}

# --- database (Aurora PostgreSQL, module.aurora — task-api's database) ---
# One cluster per environment, not a map like `services` — RDS Proxy and
# per-tenant credentials are deliberately out of scope for now, see this
# repo's Aurora module memory for why.

variable "aurora_database_name" {
  type        = string
  default     = ""
  description = "Initial database name created on the Aurora cluster. \"\" falls back to \"thor_<environment>_db\" (module.aurora computes this)."
}

variable "aurora_master_username" {
  type        = string
  default     = "thor_admin"
  description = "Master username — the password itself is AWS-managed (Secrets Manager), never set here"
}

variable "aurora_engine_version" {
  type        = string
  default     = "16.13"
  description = "Aurora PostgreSQL engine version"
}

variable "aurora_min_capacity" {
  type        = number
  default     = 0.5
  description = "Serverless v2 minimum ACU"
}

variable "aurora_max_capacity" {
  type        = number
  default     = 1
  description = "Serverless v2 maximum ACU"
}

variable "aurora_backup_retention_days" {
  type        = number
  default     = 7
  description = "Automated backup retention period"
}

variable "aurora_deletion_protection" {
  type        = bool
  default     = false
  description = "Should be true for prod, false for throwaway dev/qa environments"
}

variable "aurora_skip_final_snapshot" {
  type        = bool
  default     = true
  description = "Should be false for prod, true for throwaway dev/qa environments"
}

# --- master DB IAM app user ---

variable "master_db_app_user" {
  type        = string
  default     = "thor_app"
  description = "Postgres role (rds_iam, no password) the runtime services assume via IAM to read the Master metadata DB. Bootstrapped alongside thor_provisioner."
}

# --- tenant provisioning (Step Functions workflow, module.tenant_provisioning) ---

variable "enable_tenant_provisioning" {
  type        = bool
  default     = false
  description = "Whether to create the tenant-provisioning state machine + Lambdas. Off by default; enable per environment once the hosted zone / domain inputs are supplied."
}

variable "provisioning_db_user" {
  type        = string
  default     = "thor_provisioner"
  description = "DDL role (rds_iam, CREATEDB, CREATEROLE) the create-tenant-database Lambda assumes via IAM. Bootstrapped once against the cluster."
}

variable "metadata_writer_db_user" {
  type        = string
  default     = "thor_metadata_writer"
  description = "Metadata-write role (rds_iam, DML on the auth tables only) the seed + finalize-routing Lambdas assume via IAM. Bootstrapped once against the cluster."
}

variable "tenant_provisioning_source_dir" {
  type        = string
  default     = ""
  description = "Absolute path to the published Thor.TenantProvisioning Lambda artifact (…/backend/functions/Thor.TenantProvisioning/publish). Required when enable_tenant_provisioning is true."
}

variable "tenant_provisioning_base_domain" {
  type        = string
  default     = ""
  description = "Base domain under which tenant subdomains are created (e.g. dev.thor.example.com). Must be the zone_name of a zone in hosted_zones — its hosted zone ID is what the configure-subdomain Lambda writes records into. Required when enable_tenant_provisioning is true."
}

variable "tenant_provisioning_dns_target" {
  type        = string
  default     = ""
  description = "DNS target (CNAME value) each tenant subdomain record points at — typically the public API/CloudFront hostname. Required when enable_tenant_provisioning is true."
}

# storage / event-driven variables get added here as those modules are
# wired in below, alongside their own module blocks in main.tf.
