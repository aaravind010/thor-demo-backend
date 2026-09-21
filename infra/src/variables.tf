variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod)"
}

variable "aws_region" {
  type        = string
  description = "AWS region this environment's regional resources deploy into — root.hcl's account_map, supplied via its inputs block. Same value the generated provider block is already configured with; passed through explicitly so a certificate with scope = \"regional\" (var.hosted_zones) can be issued in it."
}

variable "global_region" {
  type        = string
  description = "The region AWS requires for CloudFront-adjacent resources, regardless of where var.aws_region puts the rest of the stack. Two things depend on it: ACM certificates a CloudFront distribution serves (CloudFront reads certs only from us-east-1) and CLOUDFRONT-scoped WAFv2 web ACLs (only creatable through the us-east-1 endpoint — every other region rejects the scope with WAFInvalidParameterException). Not a tunable: set to anything but us-east-1 and both break."
  default     = "us-east-1"
}

variable "account_id" {
  type        = string
  description = "AWS account ID"
}

# --- network ---

variable "vpc_cidr" {
  type        = string
  description = "CIDR block for the VPC"
  default     = ""
}

variable "az_count" {
  type        = number
  description = "Number of availability zones to span"
  default     = 2
}

variable "private_subnet_cidrs" {
  type        = list(string)
  description = "CIDR blocks for private subnets, one per AZ"
  default     = []
}

variable "enable_vpc_endpoints" {
  type        = bool
  description = "Create gateway/interface VPC endpoints instead of NAT Gateway egress"
  default     = true
}

# --- compute (shared ECS cluster running thor, task-api, intelligence-engine) ---

variable "enable_compute" {
  type        = bool
  description = "Whether to create the three ECS services (task defs, ECS services, NLB/target groups, per-service IAM/SG). The ECS cluster, Service Connect namespace, and ECR repos are created regardless of this flag — set false to bring up an environment's cluster/registry only, before real images exist to reference."
  default     = true
}

variable "enable_ingestion" {
  type        = bool
  description = "Whether to create the ingestion pipeline (S3 -> SQS -> EventBridge Pipe -> CreateManifest Lambda -> Step Functions -> ECS ingestion task, see modules/ingestion). The ECR repo is created regardless of this flag, so an image can be pushed before turning it on — everything else stays off until deliberately enabled."
  default     = false
}

variable "services" {
  description = "Per-service configuration for the shared ECS cluster, keyed by service name. Must define thor-api, task-api, and intelligence-engine, with thor-api the only one setting expose_via_nlb = true (a private NLB reached via VPC Link from API Gateway, not the internet directly — no ALB). deployment_strategy=BLUE_GREEN is a per-deploy toggle (reserved for DB-schema-change deploys per deployment_strategy_plan.md), not a fixed per-service default."
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
    expose_via_nlb        = optional(bool, false)
    nlb_listener_port     = optional(number, 80)
    deployment_strategy   = optional(string, "ROLLING")
    bake_time_in_minutes  = optional(number, 5)
  }))

  default = {
    thor-api            = { container_image = "", expose_via_nlb = true }
    task-api            = { container_image = "" }
    intelligence-engine = { container_image = "" }
  }

  validation {
    condition     = alltrue([for k in ["thor-api", "task-api", "intelligence-engine"] : contains(keys(var.services), k)])
    error_message = "var.services must define an entry for each of: thor-api, task-api, intelligence-engine."
  }

  validation {
    condition     = contains(keys(var.services), "thor-api") ? var.services["thor-api"].expose_via_nlb == true : true
    error_message = "var.services.thor-api.expose_via_nlb must be true — thor-api is the only internet-facing service; task-api and intelligence-engine must stay internal."
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

variable "create_manifest_source_dir" {
  type        = string
  description = "Absolute path to CreateManifest's dotnet publish output"
}

variable "secrets_recovery_window_in_days" {
  type        = number
  description = "Days the authorizer salt secret stays recoverable after a destroy. 0 = delete immediately."
  default     = 30
}

# --- route53 + acm (hosted zones with their certificates nested) ---
# hosted_zones nests certificates under their zone for readability here; modules/route53 and
# modules/acm both stay flat and generic — infra/src/main.tf flattens this at the boundary.

variable "enable_route53" {
  type        = bool
  description = "Whether to create module.route53/module.acm at all. false leaves every custom-domain input (frontend_certificate_key, api_cdn_certificate_key, backend_certificate_key) inert regardless of what they're set to."
  default     = false
}

variable "hosted_zones" {
  description = "Hosted zones this environment needs, keyed by an arbitrary logical name (e.g. \"apex\", \"thor\"), each with its own nested certificates. A zone with create_zone = false (the default is true) is looked up instead of created — for a parent zone another environment already owns. parent_zone_name, if it matches another zone's zone_name in this same map, gets this zone's NS delegation record created automatically in that parent."
  type = map(object({
    zone_name        = string
    create_zone      = optional(bool, true)
    comment          = optional(string, "")
    tags             = optional(map(string), {})
    parent_zone_name = optional(string, "")
    certificates = optional(map(object({
      domain_name               = string
      subject_alternative_names = optional(list(string), [])
      include_wildcard          = optional(bool, false)
      # Which region ACM issues this certificate in, named by what consumes it rather than by a
      # region literal — so nothing here has to be touched when var.aws_region moves.
      #
      #   "global"   -> var.global_region (us-east-1). For certificates a CloudFront distribution
      #                 serves, which CloudFront will only read from us-east-1.
      #   "regional" -> var.aws_region. For certificates a regional resource attaches, above all
      #                 the NLB's TLS listener: an ACM certificate can only be attached by a load
      #                 balancer in its own region, so a us-east-1 cert simply cannot be used by a
      #                 differently-regioned NLB.
      #
      # Defaults to "global" because most certificates here front CloudFront; the NLB's is the
      # exception and says so explicitly in each environment's terragrunt.hcl.
      scope = optional(string, "global")
    })), {})
  }))
  default = {}

  validation {
    condition = alltrue([
      for zone in values(var.hosted_zones) : alltrue([
        for cert in values(zone.certificates) : contains(["global", "regional"], cert.scope)
      ])
    ])
    error_message = "Each certificate's scope must be either \"global\" (us-east-1, for CloudFront) or \"regional\" (this stack's own region, for the NLB)."
  }
}

variable "frontend_certificate_key" {
  type        = string
  description = "Which entry of var.hosted_zones' flattened certificates (\"<zone_key>/<cert_key>\", e.g. \"thor/frontend\") covers the frontend's custom domain. \"\" (default) leaves the frontend on its own *.cloudfront.net domain. Unused when enable_route53 is false."
  default     = ""
}

# --- api cdn (CloudFront in front of the API Gateway REST API) ---

variable "api_cdn_certificate_key" {
  type        = string
  description = "Which entry of var.hosted_zones' flattened certificates (\"<zone_key>/<cert_key>\", e.g. \"thor/api\") covers the API's custom domain. \"\" (default) leaves the API CDN on its own *.cloudfront.net domain. Unused when enable_route53 is false."
  default     = ""
}

variable "api_cdn_price_class" {
  type        = string
  description = "CloudFront price class for the API's CDN distribution"
  default     = "PriceClass_100"
}

variable "api_cdn_waf_rate_limit" {
  type        = number
  description = "WAF rate-limit threshold for the API's CDN distribution: requests from a single IP in a rolling 5-minute window before it's blocked."
  default     = 2000
}

variable "backend_certificate_key" {
  type        = string
  description = "Which entry in the flattened hosted_zones certificates (\"<zone_key>/<cert_key>\") the NLB's TLS listener cert comes from, for NLB <-> ECS re-encryption. \"\" (default) leaves module.api_gateway's tls_config unset — must agree with the NLB's own TLS state, which this branch doesn't yet configure (see modules/ecs)."
  default     = ""
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
  description = "Aurora PostgreSQL engine version — must be >= 16.1 for RDS Data API support"
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

# --- neptune (graph DB for intelligence-engine/task-api edge queries) ---

variable "enable_neptune" {
  type        = bool
  description = "Whether to create the Neptune graph DB (module.neptune) — subnet group, security group + per-consumer ingress rules, and the serverless cluster/instance"
  default     = false
}

variable "neptune_engine_version" {
  type        = string
  default     = "1.4.8.0"
  description = "Neptune engine version — passed through to module.neptune"
}

variable "neptune_min_capacity" {
  type        = number
  default     = 1
  description = "Neptune Serverless v2 minimum NCU"
}

variable "neptune_max_capacity" {
  type        = number
  default     = 2
  description = "Neptune Serverless v2 maximum NCU"
}

variable "neptune_backup_retention_days" {
  type        = number
  default     = 7
  description = "Neptune automated backup retention period"
}

variable "neptune_deletion_protection" {
  type        = bool
  default     = false
  description = "Should be true for prod, false for throwaway dev/qa environments"
}

variable "neptune_skip_final_snapshot" {
  type        = bool
  default     = true
  description = "Should be false for prod, true for throwaway dev/qa environments"
}

# storage / event-driven variables get added here as those modules are
# wired in below, alongside their own module blocks in main.tf.
