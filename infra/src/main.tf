# Root module: composes every infra component for one environment into a single Terraform
# state (one per environment, via Terragrunt).

# required_providers isn't declared here — Terragrunt's generate block (root.hcl) already
# writes one into this directory via generated provider.tf, and Terraform rejects two in one
# module. required_version alone is what tflint flags as missing anyway (root main.tf only
# composes module blocks, no direct aws_* resources).
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
  private_subnet_cidrs = var.private_subnet_cidrs
  enable_vpc_endpoints = var.enable_vpc_endpoints
  tags                 = var.tags
}

locals {
  vpc_id             = module.network.vpc_id
  vpc_cidr_effective = module.network.vpc_cidr
  private_subnet_ids = module.network.private_subnet_ids

  # The one service with expose_via_nlb = true.
  public_service_name = [for k, v in var.services : k if v.expose_via_nlb][0]

  # RDS Proxy is mandatory — thor/task-api always connect through it, never straight to Aurora.
  db_host = module.rds_proxy.endpoint

  # Shared Aurora connection info, merged into thor-api/task-api only (service-specific values stay in terragrunt.hcl).
  services_with_shared_secrets = {
    for k, v in var.services : k => contains(["thor-api", "task-api"], k) ? merge(v, {
      secrets = merge(v.secrets, {
        THOR_MASTERDB_USER     = "${module.aurora.master_user_secret_arn}:username::"
        THOR_MASTERDB_PASSWORD = "${module.aurora.master_user_secret_arn}:password::"
      })
      environment_variables = merge(v.environment_variables, {
        THOR_MASTERDB_HOST     = local.db_host
        THOR_MASTERDB_DATABASE = module.aurora.database_name
        THOR_MASTERDB_PORT     = "5432"
      })
    }) : v
  }
}

# Always instantiated — enable_compute is passed through and gates services/target-group/IAM
# internally, not this module block, since the cluster/namespace/ECR repos must exist before
# enable_compute can turn on.
module "ecs" {
  source = "./modules/ecs"

  environment                  = var.environment
  enable_compute               = var.enable_compute
  vpc_id                       = local.vpc_id
  vpc_cidr                     = local.vpc_cidr_effective
  private_subnet_ids           = local.private_subnet_ids
  enable_container_insights    = var.enable_container_insights
  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  services = local.services_with_shared_secrets

  nlb_certificate_arn = local.backend_route53 != null ? local.backend_route53.certificate_arn : ""

  tags = var.tags
}

locals {
  # var.hosted_zones nests certificates under their zone for readability in terragrunt.hcl —
  # modules/route53 and modules/acm both stay flat and generic, so the flattening happens
  # here, once, at the boundary.

  # zones map for modules/route53: keyed by the real domain name (zone_name), not
  # hosted_zones' own logical keys.
  route53_zones = {
    for zone_key, zone in var.hosted_zones : zone.zone_name => {
      create_zone      = zone.create_zone
      comment          = zone.comment
      tags             = zone.tags
      parent_zone_name = zone.parent_zone_name
    }
  }

  # Every certificate across every zone, flattened into one map, keyed "<zone_key>/<cert_key>"
  # so repeated cert names (e.g. "api" in two zones) never collide. zone_name rides along on
  # each entry so the zone_id lookup below knows which zone to resolve.
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

  # scope resolves to a real region here, at the boundary, so nothing below the root module and
  # nothing in any environment's terragrunt.hcl carries a region literal — moving var.aws_region
  # moves the regional certificates with it, and leaves the CloudFront ones where AWS demands.
  certificates = {
    for key, cert in local.route53_certificates_flat : key => {
      domain_name               = cert.domain_name
      subject_alternative_names = cert.subject_alternative_names
      include_wildcard          = cert.include_wildcard
      region                    = cert.scope == "regional" ? var.aws_region : var.global_region
    }
  }

  # Every zone, not just the one each certificate was declared under — the module routes each
  # validation CNAME to whichever zone actually serves it, so a cert for a name in a delegated
  # child zone (api.dev.example.com) validates without help.
  zones = module.route53[0].zone_ids

  tags = var.tags
}

locals {
  # "" until enable_route53 is on and frontend_certificate_key points at an entry — keeps
  # module.frontend's domain_name/etc. defaulted to "" (its own "no custom domain" fallback)
  # rather than erroring.
  frontend_route53 = var.enable_route53 && var.frontend_certificate_key != "" ? {
    domain_name     = local.route53_certificates_flat[var.frontend_certificate_key].domain_name
    certificate_arn = module.acm[0].certificate_arns[var.frontend_certificate_key]
    zone_id         = module.route53[0].zone_ids[local.route53_certificates_flat[var.frontend_certificate_key].zone_name]
  } : null

  # Same pattern as frontend_route53, for the CloudFront distribution fronting the API — its
  # certificate has to be us-east-1 like any CloudFront cert (see api_cdn_certificate_key).
  api_cdn_route53 = var.enable_route53 && var.api_cdn_certificate_key != "" ? {
    domain_name     = local.route53_certificates_flat[var.api_cdn_certificate_key].domain_name
    certificate_arn = module.acm[0].certificate_arns[var.api_cdn_certificate_key]
    zone_id         = module.route53[0].zone_ids[local.route53_certificates_flat[var.api_cdn_certificate_key].zone_name]
  } : null

  # Same pattern, for the NLB's TLS listener / API Gateway's tls_config — only domain_name is consumed so far.
  backend_route53 = var.enable_route53 && var.backend_certificate_key != "" ? {
    domain_name     = local.route53_certificates_flat[var.backend_certificate_key].domain_name
    certificate_arn = module.acm[0].certificate_arns[var.backend_certificate_key]
    zone_id         = module.route53[0].zone_ids[local.route53_certificates_flat[var.backend_certificate_key].zone_name]
  } : null
}

# Private S3 + CloudFront (OAC) for the static frontend SPA — independent of network/compute. The deploy role that syncs to it is bootstrapped separately by scripts/create-deploy-role.sh


# Independent of network/compute — a private S3 bucket + CloudFront (OAC) distribution for the
# static thor-demo-frontend SPA. The GitHub Actions deploy role that syncs to it is bootstrapped
# separately by scripts/create-deploy-role.sh (see that script's header — its CloudFront
# distribution ID isn't known until this applies).
module "frontend" {
  count = var.enable_frontend ? 1 : 0

  source = "./modules/frontend"

  environment = var.environment
  price_class = var.frontend_price_class

  # Only for the module's CLOUDFRONT-scoped WAF ACL, which AWS will not create anywhere else.
  global_region = var.global_region

  domain_name         = local.frontend_route53 != null ? local.frontend_route53.domain_name : ""
  acm_certificate_arn = local.frontend_route53 != null ? local.frontend_route53.certificate_arn : ""
  zone_id             = local.frontend_route53 != null ? local.frontend_route53.zone_id : ""

  tags = var.tags
}

# Gated by enable_compute — needs thor-api's NLB listener to exist first (module.ecs.nlb_arns
# and nlb_dns_names populate only once local.public_services is non-empty).
module "api_gateway" {
  count = var.enable_compute ? 1 : 0

  source = "./modules/api_gateway"

  environment            = var.environment
  service_name           = local.public_service_name
  nlb_listener_arn       = module.ecs.nlb_listener_arns[local.public_service_name]
  nlb_security_group_id  = module.ecs.nlb_security_group_ids[local.public_service_name]
  nlb_listener_port      = var.services[local.public_service_name].nlb_listener_port
  vpc_id                 = local.vpc_id
  private_subnet_ids     = local.private_subnet_ids

  authorizer_lambda_invoke_arn    = module.lambda.lambda_invoke_arn
  authorizer_lambda_function_name = module.lambda.lambda_function_name

  # Must agree with the NLB's own TLS state (modules/ecs doesn't configure that on this branch yet).
  tls_server_name = local.backend_route53 != null ? local.backend_route53.domain_name : ""

  # Only for the module's CLOUDFRONT-scoped WAF ACL, which AWS will not create anywhere else.
  global_region = var.global_region

  # The public hostname belongs to the CloudFront distribution, so its certificate has to be
  # us-east-1 like any CloudFront cert — see api_cdn_certificate_key.
  domain_name         = local.api_cdn_route53 != null ? local.api_cdn_route53.domain_name : ""
  acm_certificate_arn = local.api_cdn_route53 != null ? local.api_cdn_route53.certificate_arn : ""
  zone_id             = local.api_cdn_route53 != null ? local.api_cdn_route53.zone_id : ""
  cdn_price_class     = var.api_cdn_price_class
  cdn_waf_rate_limit  = var.api_cdn_waf_rate_limit

  tags = var.tags
}

# Holds the shared salt Thor.Authorizer's PBKDF2 hasher needs — separate from Aurora's own secret.
module "secrets" {
  source = "./modules/secrets"

  environment = var.environment
  recovery_window_in_days = var.secrets_recovery_window_in_days
  tags = var.tags
}

# Validates API keys against Aurora via RDS Data API; the Lambda runs in-VPC.
module "lambda" {
  source = "./modules/lambda"

  environment          = var.environment
  aurora_cluster_arn   = module.aurora.cluster_arn
  aurora_secret_arn    = module.aurora.master_user_secret_arn
  aurora_database_name = module.aurora.database_name

  vpc_id                          = local.vpc_id
  private_subnet_ids              = local.private_subnet_ids
  vpc_endpoints_security_group_id = module.network.vpc_endpoints_security_group_id
  iam_permissions_boundary_arn    = local.iam_permissions_boundary_arn

  authorizer_salt_secret_arn = module.secrets.authorizer_salt_secret_arn

  runtime               = var.authorizer_lambda_runtime
  timeout               = var.authorizer_lambda_timeout
  memory_size           = var.authorizer_lambda_memory_size
  authorizer_source_dir = var.authorizer_source_dir

  tags = var.tags
}

# task-api's database — always instantiated (like module.ecs's cluster/ECR) so migrations/seeding can run before enable_compute turns on. task_api_security_group_id is null until then; the ingress rule is just skipped, not an error.
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

  aurora_cluster_identifier = module.aurora.cluster_identifier
  aurora_secret_arn         = module.aurora.master_user_secret_arn

  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  allowed_security_group_ids = var.enable_compute ? {
    thor-api = module.ecs.service_security_group_ids["thor-api"]
    task-api = module.ecs.service_security_group_ids["task-api"]
  } : {}

  tags = var.tags
}

# Neptune remains future work — see this repo's CI/CD memory for why it's deliberately excluded from this pass.
