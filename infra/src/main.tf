# Root module composing every infra component for one environment into a single Terraform state, one per environment via Terragrunt.

# required_providers deliberately isn't declared here — Terragrunt's own
# generate block (root.hcl) already writes a required_providers block into
# this same directory via its generated provider.tf, and Terraform rejects
# two required_providers blocks in one module. required_version alone is
# what tflint actually flags as missing for this file anyway (root main.tf
# only composes module blocks, no direct aws_* resources of its own).
terraform {
  required_version = ">= 1.15"
}

# Region of the configured provider (set in root.hcl) — used for IAM DB-auth env vars/ARNs.
data "aws_region" "current" {}

# Disabled via enable_network when this environment borrows dev's VPC instead of creating its own (e.g. qa).
module "network" {
  count = var.enable_network ? 1 : 0

  source = "./modules/network"

  environment          = var.environment
  vpc_cidr             = var.vpc_cidr
  az_count             = var.az_count
  public_subnet_cidrs  = var.public_subnet_cidrs
  private_subnet_cidrs = var.private_subnet_cidrs
  enable_vpc_endpoints = var.enable_vpc_endpoints
  tags                 = var.tags
}

locals {
  vpc_id             = var.enable_network ? module.network[0].vpc_id : var.dev_vpc_id
  vpc_cidr_effective = var.enable_network ? module.network[0].vpc_cidr : var.dev_vpc_cidr
  public_subnet_ids  = var.enable_network ? module.network[0].public_subnet_ids : var.dev_public_subnet_ids
  private_subnet_ids = var.enable_network ? module.network[0].private_subnet_ids : var.dev_private_subnet_ids

  # RDS Proxy is mandatory — thor/task-api always connect through it, never straight to Aurora.
  db_host = module.rds_proxy.endpoint

  # Shared Aurora connection info, merged into the .NET services that read the Master DB
  # (service-specific values stay in terragrunt.hcl). Auth is RDS IAM (no secret): the task
  # role mints a token as master_db_app_user. THOR_MASTERDB_* names match Thor.TaskAPI's Program.cs.
  services_with_shared_secrets = {
    for k, v in var.services : k => contains(["thor-api", "task-api"], k) ? merge(v, {
      environment_variables = merge(v.environment_variables, {
        THOR_MASTERDB_HOST     = local.db_host
        THOR_MASTERDB_DATABASE = module.aurora.database_name
        THOR_MASTERDB_PORT     = "5432"
        THOR_MASTERDB_REGION   = data.aws_region.current.region
        THOR_MASTERDB_USER     = var.master_db_app_user
      })
    }) : v
  }
  # The one service with expose_publicly = true.
  public_service_name = [for k, v in var.services : k if v.expose_publicly][0]
}

# Always instantiated — enable_compute is passed through and gates the services/target-group/IAM internally, not this module block, since the cluster/namespace/ECR repos must exist before enable_compute can turn on.
module "ecs" {
  source = "./modules/ecs"

  environment               = var.environment
  enable_compute            = var.enable_compute
  vpc_id                    = local.vpc_id
  vpc_cidr                  = local.vpc_cidr_effective
  private_subnet_ids        = local.private_subnet_ids
  enable_container_insights = var.enable_container_insights

  services = local.services_with_shared_secrets

  # RDS IAM: the .NET services read the Master DB and connect to tenant DBs, so their task roles
  # need rds-db:connect. intelligence-engine reaches tenant DBs (ro) via IAM too.
  aurora_cluster_resource_id = module.aurora.cluster_resource_id
  master_db_app_user         = var.master_db_app_user
  db_access_service_keys     = ["thor-api", "task-api", "intelligence-engine"]

  tags = var.tags
}

# Independent of network/compute — a private S3 bucket + CloudFront (OAC)
# distribution for the static thor-demo-frontend SPA. The GitHub Actions
# deploy role that syncs to it is bootstrapped separately by
# scripts/create-deploy-role.sh, not created here — see that script's header
# for why (its CloudFront distribution ID isn't known until this applies).
module "frontend" {
  count = var.enable_frontend ? 1 : 0

  source = "./modules/frontend"

  environment = var.environment
  price_class = var.frontend_price_class

  tags = var.tags
}

# Gated by enable_compute — needs thor-api's NLB listener to exist first (module.ecs.nlb_listener_arns is only populated once local.public_services is non-empty).
module "api_gateway" {
  count = var.enable_compute ? 1 : 0

  source = "./modules/api_gateway"

  environment        = var.environment
  service_name       = local.public_service_name
  nlb_listener_arn   = module.ecs.nlb_listener_arns[local.public_service_name]
  vpc_id             = local.vpc_id
  private_subnet_ids = local.private_subnet_ids

  authorizer_lambda_invoke_arn    = module.lambda.lambda_invoke_arn
  authorizer_lambda_function_name = module.lambda.lambda_function_name

  tags = var.tags
}

# Holds the shared salt Thor.Authorizer's PBKDF2 hasher needs — separate from Aurora's own secret.
module "secrets" {
  source = "./modules/secrets"

  environment = var.environment
  tags        = var.tags
}

# Resolves subdomain -> tenant routing from the Master DB. Runs in-VPC and connects through the
# RDS Proxy with an RDS IAM token as the read-only thor_authorizer role — no password, no Data API.
module "lambda" {
  source = "./modules/lambda"

  environment                = var.environment
  vpc_id                     = local.vpc_id
  vpc_cidr                   = local.vpc_cidr_effective
  private_subnet_ids         = local.private_subnet_ids
  aurora_database_name       = module.aurora.database_name
  aurora_cluster_resource_id = module.aurora.cluster_resource_id
  authorizer_db_user         = var.authorizer_db_user

  rds_proxy_endpoint          = module.rds_proxy.endpoint
  rds_proxy_security_group_id = module.rds_proxy.rds_proxy_security_group_id

  authorizer_salt_secret_arn = module.secrets.authorizer_salt_secret_arn

  runtime               = var.authorizer_lambda_runtime
  timeout               = var.authorizer_lambda_timeout
  memory_size           = var.authorizer_lambda_memory_size
  authorizer_source_dir = var.authorizer_source_dir

  tags = var.tags
}

# One-time (re-runnable) in-VPC bootstrap of the platform's least-privilege DB roles. Runs as the
# AWS-managed Aurora master user — the only place master creds are ever used — and is invoked at
# apply by an aws_lambda_invocation inside the module.
module "db_bootstrap" {
  source = "./modules/db_bootstrap"

  environment        = var.environment
  vpc_id             = local.vpc_id
  vpc_cidr           = local.vpc_cidr_effective
  private_subnet_ids = local.private_subnet_ids

  aurora_security_group_id = module.aurora.security_group_id
  aurora_writer_endpoint   = module.aurora.endpoint
  aurora_database_name     = module.aurora.database_name
  master_user_secret_arn   = module.aurora.master_user_secret_arn

  source_dir = var.db_bootstrap_source_dir

  # The invocation connects to the DB at apply, so wait for the whole aurora module — the cluster
  # endpoint output alone doesn't order this after the instance being ready to accept connections.
  depends_on = [module.aurora]

  tags = var.tags
}

# task-api's database. Always instantiated, same reasoning as module.ecs's
# cluster/namespace/ECR repos — a database shouldn't require application
# compute to exist first, and standing it up early lets migrations/seeding
# happen before enable_compute ever turns on. Only RDS Proxy reaches Aurora
# directly — thor/task-api go through it instead
module "aurora" {
  source = "./modules/aurora"

  environment                 = var.environment
  vpc_id                      = local.vpc_id
  vpc_cidr                    = local.vpc_cidr_effective
  private_subnet_ids          = local.private_subnet_ids
  rds_proxy_security_group_id = module.rds_proxy.rds_proxy_security_group_id

  database_name         = var.aurora_database_name
  master_username       = var.aurora_master_username
  engine_version        = var.aurora_engine_version
  min_capacity          = var.aurora_min_capacity
  max_capacity          = var.aurora_max_capacity
  backup_retention_days = var.aurora_backup_retention_days
  deletion_protection   = var.aurora_deletion_protection
  skip_final_snapshot   = var.aurora_skip_final_snapshot

  tags = var.tags
}

# Connection pooling in front of Aurora — thor/task-api connect here instead of Aurora's own endpoint (see local.db_host).
module "rds_proxy" {
  source = "./modules/rds_proxy"

  environment        = var.environment
  vpc_id             = local.vpc_id
  vpc_cidr           = local.vpc_cidr_effective
  private_subnet_ids = local.private_subnet_ids

  aurora_cluster_identifier  = module.aurora.cluster_identifier
  aurora_cluster_resource_id = module.aurora.cluster_resource_id

  allowed_security_group_ids = var.enable_compute ? {
    thor                = module.ecs.service_security_group_ids["thor-api"]
    task-api            = module.ecs.service_security_group_ids["task-api"]
    intelligence-engine = module.ecs.service_security_group_ids["intelligence-engine"]
  } : {}

  tags = var.tags
}

# Tenant onboarding workflow (ADR §11) — Step Functions + 6 Lambdas that create the tenant DB,
# its IAM roles, Cognito pool, subdomain, and routing row. Gated off by default; enabled per env.
module "tenant_provisioning" {
  count = var.enable_tenant_provisioning ? 1 : 0

  source = "./modules/tenant_provisioning"

  environment        = var.environment
  vpc_id             = local.vpc_id
  vpc_cidr           = local.vpc_cidr_effective
  private_subnet_ids = local.private_subnet_ids

  aurora_security_group_id   = module.aurora.security_group_id
  aurora_writer_endpoint     = module.aurora.endpoint
  aurora_database_name       = module.aurora.database_name
  aurora_cluster_resource_id = module.aurora.cluster_resource_id

  # Runtime routing endpoint written into each tenant row = the IAM RDS Proxy.
  tenant_routing_endpoint = module.rds_proxy.endpoint
  provisioning_db_user    = var.provisioning_db_user
  metadata_writer_db_user = var.metadata_writer_db_user

  source_dir = var.tenant_provisioning_source_dir

  hosted_zone_id = var.tenant_provisioning_hosted_zone_id
  base_domain    = var.tenant_provisioning_base_domain
  dns_target     = var.tenant_provisioning_dns_target

  tags = var.tags
}

# Neptune remains future work — see this repo's CI/CD memory for why it's deliberately excluded from this pass.
