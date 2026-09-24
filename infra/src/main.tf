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

  # Shared Aurora connection info for the .NET services that read the Master DB. Auth is RDS IAM
  # (no secret): the task role mints a token as master_db_app_user. THOR_MASTERDB_* names match
  # Thor.TaskAPI's Program.cs.
  masterdb_environment = {
    THOR_MASTERDB_HOST     = local.db_host
    THOR_MASTERDB_DATABASE = module.aurora.database_name
    THOR_MASTERDB_PORT     = "5432"
    THOR_MASTERDB_REGION   = var.aws_region
    THOR_MASTERDB_USER     = var.master_db_app_user
  }

  # The rest of the config each service's Program.cs reads at startup. These live here rather than
  # in each environment's terragrunt.hcl because every value is a module output or a Service
  # Connect alias — there is nothing environment-specific to keep in sync across three files.
  # Every name below is required: the services fail closed at startup without it (ADR §5/§6).
  # tomap() on each entry, here and below: without it the two entries are objects with different
  # attribute names, which won't unify into the map type lookup() needs.
  service_app_environment = {
    thor-api = tomap({
      # Service Connect client_alias — modules/ecs names each service's alias after its map key.
      THOR_INTELLIGENCE_ENGINE_GRPC_ADDRESS = "https://intelligence-engine:${var.services["intelligence-engine"].container_port}"

      # Thor.Api's appsettings.json ships the local-dev destination (http://localhost:5081) for the
      # /task-api/* proxy, which resolves to nothing inside the deployed container. Override it with
      # task-api's Service Connect alias rather than editing appsettings, keeping the deployed
      # address environment config (ADR §10.1's "never hard-coded" rule applies here too).
      "ReverseProxy__Clusters__task-api-cluster__Destinations__primary__Address" = "https://task-api:${var.services["task-api"].container_port}/"

      # task-api's Dockerfile bakes a self-signed cert with CN=task-api.internal, which matches
      # neither the Service Connect alias nor any trusted chain — the same tradeoff Thor.Api's gRPC
      # client already makes for intelligence-engine, and the NLB->ECS leg is VPC-internal.
      "ReverseProxy__Clusters__task-api-cluster__HttpClient__DangerousAcceptAnyServerCertificate" = "true"
    })
    task-api = tomap({
      THOR_UPLOADS_BUCKET = module.uploads.bucket_name
    })
  }

  # Secret material never lands in a task definition as plaintext — ECS resolves each valueFrom at
  # task start using the execution role. The ":<json-key>::" suffix selects one field of the
  # keypair secret's JSON; the pepper secret is consumed whole. Only thor-api ever sees the
  # private half (ADR §5.2) — a compromised task-api can verify connector tokens, never mint them.
  service_app_secrets = {
    thor-api = tomap({
      THOR_TASKAPI_JWT_PRIVATE_KEY = "${module.secrets.connector_jwt_secret_arn}:private_key::"
      THOR_API_KEY_PEPPER          = module.secrets.api_key_pepper_secret_arn
    })
    task-api = tomap({
      THOR_TASKAPI_JWT_PUBLIC_KEY = "${module.secrets.connector_jwt_secret_arn}:public_key::"
    })
  }

  # Bare ARNs (no json-key suffix) so the execution role's GetSecretValue grant actually matches.
  service_secret_arns = {
    thor-api = [module.secrets.connector_jwt_secret_arn, module.secrets.api_key_pepper_secret_arn]
    task-api = [module.secrets.connector_jwt_secret_arn]
  }

  services_with_shared_secrets = {
    for k, v in var.services : k => merge(v, {
      environment_variables = merge(
        v.environment_variables,
        contains(["thor-api", "task-api"], k) ? local.masterdb_environment : {},
        lookup(local.service_app_environment, k, {}),
      )
      secrets = merge(v.secrets, lookup(local.service_app_secrets, k, {}))
    })
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
  
  # RDS IAM: the .NET services read the Master DB and connect to tenant DBs, so their task roles
  # need rds-db:connect. intelligence-engine reaches tenant DBs (ro) via IAM too. Scoped to the
  # proxy, since local.db_host is the proxy endpoint — see the module's own iam.tf comment.
  rds_proxy_resource_id  = module.rds_proxy.proxy_resource_id
  master_db_app_user     = var.master_db_app_user
  db_access_service_keys = ["thor-api", "task-api", "intelligence-engine"]

  # Runtime grants for the config above: GetSecretValue on whatever each service's `secrets` map
  # resolves, and S3 writes for the presigned uploads task-api issues.
  execution_secret_arns = local.service_secret_arns
  uploads_bucket_arn    = module.uploads.bucket_arn

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

# Tenant scan-data uploads. Independent of compute — task-api only ever presigns against it, so
# the bucket can exist (and be lifecycle-managed) before any service is running.
module "uploads" {
  source = "./modules/uploads"

  environment = var.environment
  tags        = var.tags
}

# Holds the shared salt Thor.Authorizer's PBKDF2 hasher needs — separate from Aurora's own secret.
module "secrets" {
  source = "./modules/secrets"

  environment = var.environment
  recovery_window_in_days = var.secrets_recovery_window_in_days
  tags = var.tags
}

# Resolves subdomain -> tenant routing from the Master DB. Runs in-VPC and connects through the
# RDS Proxy with an RDS IAM token as the read-only thor_authorizer role — no password, no Data API.
module "lambda" {
  source = "./modules/lambda"

  environment           = var.environment
  vpc_id                = local.vpc_id
  vpc_cidr              = local.vpc_cidr_effective
  private_subnet_ids    = local.private_subnet_ids
  aurora_database_name  = module.aurora.database_name
  rds_proxy_resource_id = module.rds_proxy.proxy_resource_id
  authorizer_db_user    = var.authorizer_db_user

  rds_proxy_endpoint          = module.rds_proxy.endpoint
  rds_proxy_security_group_id = module.rds_proxy.rds_proxy_security_group_id

  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  authorizer_salt_secret_arn = module.secrets.authorizer_salt_secret_arn
  connector_jwt_public_key   = var.connector_jwt_public_key

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

  source_dir                    = var.db_bootstrap_source_dir
  skip_migrations               = var.db_bootstrap_skip_migrations
  iam_permissions_boundary_arn  = local.iam_permissions_boundary_arn

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

  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  allowed_security_group_ids = merge(
    var.enable_compute ? {
      thor-api = module.ecs.service_security_group_ids["thor-api"]
      task-api = module.ecs.service_security_group_ids["task-api"]
    } : {},
    # The ingestion ECS tasks and Lambda functions (CreateManifest, driver, per-step) all sit on
    # this one SG and all read the Master DB through the proxy.
    var.enable_ingestion ? {
      ingestion = module.ingestion.task_security_group_id
    } : {},
    # The tenant-migration runner reads routing and diffs/applies every tenant DB via the proxy.
    var.enable_tenant_migration ? {
      tenant-migration = module.tenant_migration[0].task_security_group_id
    } : {}
  )

  tags = var.tags
}

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
  ingestion_driver_source_dir  = var.ingestion_driver_source_dir

  # RDS IAM: every ingestion compute target reads the Master DB for routing and then connects to
  # the tenant DB, so they all need rds-db:connect. Scoped to the proxy, since db_host is the proxy
  # endpoint — same reasoning as the ecs module's wiring above.
  db_host               = module.rds_proxy.endpoint
  db_name               = module.aurora.database_name
  rds_proxy_resource_id = module.rds_proxy.proxy_resource_id
  master_db_app_user    = var.master_db_app_user

  neptune_endpoint            = var.enable_neptune ? module.neptune[0].endpoint : ""
  neptune_cluster_resource_id = var.enable_neptune ? module.neptune[0].cluster_resource_id : ""
  neptune_loader_role_arn     = var.enable_neptune ? module.neptune[0].bulk_load_role_arn : ""

  tags = var.tags
}

# Graph DB for intelligence-engine/task-api's edge/relationship queries.
module "neptune" {
  count = var.enable_neptune ? 1 : 0

  source = "./modules/neptune"
  environment        = var.environment
  vpc_id             = local.vpc_id
  vpc_cidr           = local.vpc_cidr_effective
  private_subnet_ids = local.private_subnet_ids
  create_ingress     = var.enable_compute || var.enable_ingestion

  consumer_security_group_ids = merge(
    var.enable_compute ? {
      intelligence-engine = module.ecs.service_security_group_ids["intelligence-engine"]
      task-api            = module.ecs.service_security_group_ids["task-api"]
    } : {},
    var.enable_ingestion ? {
      ingestion = module.ingestion.task_security_group_id
    } : {}
  )

  engine_version        = var.neptune_engine_version
  min_capacity          = var.neptune_min_capacity
  max_capacity          = var.neptune_max_capacity
  backup_retention_days = var.neptune_backup_retention_days
  deletion_protection   = var.neptune_deletion_protection
  skip_final_snapshot   = var.neptune_skip_final_snapshot

  # The loader reads the CSVs module.ingestion's graph-load-start writes to its bucket.
  create_bulk_load_role        = var.enable_ingestion
  bulk_load_bucket_arn         = var.enable_ingestion ? module.ingestion.bucket_arn : ""
  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  tags = var.tags
}

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

  source_dir                   = var.tenant_provisioning_source_dir
  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  # Tenant records land in the zone whose name is the base domain (e.g. dev.spheredev.ai), so
  # enabling this requires enable_route53 and that zone in hosted_zones.
  hosted_zone_id = module.route53[0].zone_ids[var.tenant_provisioning_base_domain]
  base_domain    = var.tenant_provisioning_base_domain
  dns_target     = var.tenant_provisioning_dns_target

  tags = var.tags
}

# Expand-phase tenant schema migrations (migrations/tenant, run by .github/workflows/tenant-migrations.yml).
module "tenant_migration" {
  count = var.enable_tenant_migration ? 1 : 0

  source = "./modules/tenant_migration"

  environment                  = var.environment
  account_id                   = var.account_id
  aws_region                   = var.aws_region
  vpc_id                       = local.vpc_id
  private_subnet_ids           = local.private_subnet_ids
  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  # The OIDC role every workflow (and infra.yml's Terraform) runs as — deploy-<environment>,
  # created outside Terraform (docs/Infra_pipeline/infra_pipeline_setup_guide.md, Step 3). Only
  # it, the runner and the state machine can reach the migration bucket.
  github_oidc_role_arn = "arn:aws:iam::${var.account_id}:role/deploy-${var.environment}"

  # The runner reads tenant routing from the Master DB and reaches every tenant DB through the
  # proxy (tenant_routing.cluster_endpoint), so rds-db:connect is proxy-scoped.
  db_host               = module.rds_proxy.endpoint
  db_name               = module.aurora.database_name
  rds_proxy_resource_id = module.rds_proxy.proxy_resource_id
  master_db_app_user    = var.master_db_app_user

  state_machine_definition_path = var.tenant_migration_asl_path

  tags = var.tags
}

# Writes deployment seed data into the Master DB (Thor.MasterDbSeed). Runs in-VPC and connects
# through the RDS Proxy with an RDS IAM token as the least-privilege thor_master_seed role —
# same invoke-at-apply shape as module.db_bootstrap, which creates that role first.
#
module "master_db_seed" {
  source = "./modules/master_db_seed"

  environment        = var.environment
  vpc_id             = local.vpc_id
  vpc_cidr           = local.vpc_cidr_effective
  private_subnet_ids = local.private_subnet_ids

  aurora_database_name  = module.aurora.database_name
  rds_proxy_resource_id = module.rds_proxy.proxy_resource_id
  master_seed_db_user   = var.master_seed_db_user

  rds_proxy_endpoint          = module.rds_proxy.endpoint
  rds_proxy_security_group_id = module.rds_proxy.rds_proxy_security_group_id

  source_dir                   = var.master_db_seed_source_dir
  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  # Needs thor_master_seed to already exist (created by db_bootstrap's db-roles.sql) and the
  # proxy to be reachable.
  depends_on = [module.db_bootstrap, module.rds_proxy]

  tags = var.tags
}
