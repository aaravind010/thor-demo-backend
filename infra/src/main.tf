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

locals {
  iam_permissions_boundary_arn = "arn:aws:iam::${var.account_id}:policy/thor-${var.environment}-role-boundary"
}

module "network" {
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
  vpc_id             = module.network.vpc_id
  vpc_cidr_effective = module.network.vpc_cidr
  public_subnet_ids  = module.network.public_subnet_ids
  private_subnet_ids = module.network.private_subnet_ids

  # The one service with expose_via_nlb = true.
  public_service_name = [for k, v in var.services : k if v.expose_via_nlb][0]
}

# Always instantiated — enable_compute is passed through and gates the services/target-group/IAM internally, not this module block, since the cluster/namespace/ECR repos must exist before enable_compute can turn on.
module "ecs" {
  source = "./modules/ecs"

  environment                  = var.environment
  enable_compute               = var.enable_compute
  vpc_id                       = local.vpc_id
  vpc_cidr                     = local.vpc_cidr_effective
  private_subnet_ids           = local.private_subnet_ids
  enable_container_insights    = var.enable_container_insights
  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  services = var.services

  nlb_certificate_arn = local.backend_route53 != null ? local.backend_route53.certificate_arn : ""

  tags = var.tags
}

locals {
  # var.hosted_zones nests certificates under their zone for readability in terragrunt.hcl — modules/route53 and modules/acm both stay flat and generic, so this flattening happens here, once, at the boundary.

  # zones map for modules/route53: keyed by the real domain name (zone_name), not hosted_zones' own logical keys.
  route53_zones = {
    for zone_key, zone in var.hosted_zones : zone.zone_name => {
      create_zone = zone.create_zone
      comment     = zone.comment
      tags        = zone.tags
    }
  }

  # Every certificate across every zone, flattened into one map. Keyed by "<zone_key>/<cert_key>" so short, repeated cert names (e.g. "api" in two different zones) never collide once flattened. zone_name rides along on each entry so the zone_id lookup below knows which zone to resolve.
  route53_certificates_flat = merge([
    for zone_key, zone in var.hosted_zones : {
      for cert_key, cert in zone.certificates : "${zone_key}/${cert_key}" => merge(cert, {
        zone_name = zone.zone_name
      })
    }
  ]...)
}

module "route53" {
  count = var.enable_route53 ? 1 : 0

  source = "./modules/route53"

  zones = local.route53_zones
  tags  = var.tags
}

module "acm" {
  count = var.enable_route53 ? 1 : 0

  source = "./modules/acm"

  certificates = {
    for key, cert in local.route53_certificates_flat : key => {
      domain_name               = cert.domain_name
      subject_alternative_names = cert.subject_alternative_names
      zone_id                   = module.route53[0].zone_ids[cert.zone_name]
    }
  }

  tags = var.tags
}

locals {
  # "" until enable_route53 is on and frontend_certificate_key actually points at an entry — keeps module.frontend's domain_name/etc. defaulted to "" (its own "no custom domain" fallback) rather than erroring.
  frontend_route53 = var.enable_route53 && var.frontend_certificate_key != "" ? {
    domain_name     = local.route53_certificates_flat[var.frontend_certificate_key].domain_name
    certificate_arn = module.acm[0].certificate_arns[var.frontend_certificate_key]
    zone_id         = module.route53[0].zone_ids[local.route53_certificates_flat[var.frontend_certificate_key].zone_name]
  } : null

  # Same pattern as frontend_route53 above, for api_gateway's custom domain mapping.
  apigw_route53 = var.enable_route53 && var.api_gateway_certificate_key != "" ? {
    domain_name     = local.route53_certificates_flat[var.api_gateway_certificate_key].domain_name
    certificate_arn = module.acm[0].certificate_arns[var.api_gateway_certificate_key]
    zone_id         = module.route53[0].zone_ids[local.route53_certificates_flat[var.api_gateway_certificate_key].zone_name]
  } : null

  # Same pattern again, for the NLB's TLS listener (NLB <-> ECS re-encryption) and API Gateway's
  # matching tls_config. modules/ecs doesn't consume this on this branch yet — only domain_name
  # feeds module.api_gateway's tls_server_name for now.
  backend_route53 = var.enable_route53 && var.backend_certificate_key != "" ? {
    domain_name     = local.route53_certificates_flat[var.backend_certificate_key].domain_name
    certificate_arn = module.acm[0].certificate_arns[var.backend_certificate_key]
    zone_id         = module.route53[0].zone_ids[local.route53_certificates_flat[var.backend_certificate_key].zone_name]
  } : null
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

  domain_name         = local.frontend_route53 != null ? local.frontend_route53.domain_name : ""
  acm_certificate_arn = local.frontend_route53 != null ? local.frontend_route53.certificate_arn : ""
  zone_id             = local.frontend_route53 != null ? local.frontend_route53.zone_id : ""

  tags = var.tags
}

# Gated by enable_compute — needs thor-api's NLB listener to exist first (module.ecs.nlb_arns/nlb_dns_names are only populated once local.public_services is non-empty).
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

  domain_name         = local.apigw_route53 != null ? local.apigw_route53.domain_name : ""
  acm_certificate_arn = local.apigw_route53 != null ? local.apigw_route53.certificate_arn : ""
  zone_id             = local.apigw_route53 != null ? local.apigw_route53.zone_id : ""

  # Must agree with the NLB's own TLS state (modules/ecs doesn't configure that on this branch yet).
  tls_server_name = local.backend_route53 != null ? local.backend_route53.domain_name : ""

  tags = var.tags
}

# Holds the shared salt Thor.Authorizer's PBKDF2 hasher needs — separate from Aurora's own secret.
module "secrets" {
  source = "./modules/secrets"

  environment = var.environment
  tags        = var.tags
}

# Validates API keys against Aurora via RDS Data API — no VPC needed.
module "lambda" {
  source = "./modules/lambda"

  environment          = var.environment
  aurora_cluster_arn   = module.aurora.cluster_arn
  aurora_secret_arn    = module.aurora.master_user_secret_arn
  aurora_database_name = module.aurora.database_name

  authorizer_salt_secret_arn = module.secrets.authorizer_salt_secret_arn

  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  runtime               = var.authorizer_lambda_runtime
  timeout               = var.authorizer_lambda_timeout
  memory_size           = var.authorizer_lambda_memory_size
  authorizer_source_dir = var.authorizer_source_dir

  tags = var.tags
}

# task-api's database. Always instantiated, same reasoning as module.ecs's
# cluster/namespace/ECR repos — a database shouldn't require application
# compute to exist first, and standing it up early lets migrations/seeding
# happen before enable_compute ever turns on. task_api_security_group_id is
# null until enable_compute creates task-api's own security group; the
# module's ingress rule is skipped entirely until then, not an error.
module "aurora" {
  source = "./modules/aurora"

  environment                = var.environment
  vpc_id                     = local.vpc_id
  vpc_cidr                   = local.vpc_cidr_effective
  private_subnet_ids         = local.private_subnet_ids
  create_task_api_ingress    = var.enable_compute
  task_api_security_group_id = var.enable_compute ? module.ecs.service_security_group_ids["task-api"] : null

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

# Ingestion pipeline: S3 -> SQS -> EventBridge Pipe -> CreateManifest (Lambda) -> Step Functions -> ECS task on its own
# dedicated cluster, isolated from module.ecs's shared cluster, then decides retry-vs-DLQ.
module "ingestion" {
  source = "./modules/ingestion"

  environment                  = var.environment
  account_id                   = var.account_id
  aws_region                   = var.aws_region
  enable_ingestion             = var.enable_ingestion
  vpc_id                       = local.vpc_id
  private_subnet_ids           = local.private_subnet_ids
  enable_container_insights    = var.enable_container_insights
  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn
  create_manifest_source_dir   = var.create_manifest_source_dir

  tags = var.tags
}

# RDS Proxy and Neptune remain future work — see this repo's CI/CD memory for why they're deliberately excluded from this pass.
