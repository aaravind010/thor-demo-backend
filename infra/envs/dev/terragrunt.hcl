include "root" {
  path = find_in_parent_folders("root.hcl")
}

terraform {
  source = "../../src"
}

locals {
  apex_domain = "sphereboarddev.ai" # Follows prod's confirmed root_domain = "sphereboard.ai" naming
}

# Every value the root module accepts is spelled out below instead of relying on a default in infra/src/variables.tf; only `environment` is left out, since infra/root.hcl already supplies it for every environment.
inputs = {
  # --- network ---
  vpc_cidr             = "10.0.0.0/16"
  az_count             = 2
  private_subnet_cidrs = ["10.0.10.0/24", "10.0.11.0/24"]
  enable_vpc_endpoints = true
  public_subnet_cidrs  = ["10.0.0.0/24", "10.0.1.0/24"]
  nat_gateway_mode     = "single" # dev target: "single"

  # --- DNS Firewall default deny (active only while nat_gateway_mode != "none") ---
  # Package registries are always blocked; AWS, VPC-internal and egress_allowed_domains names always resolve.
  # dns_firewall_default_action decides what happens to every other name:
  #   "ALERT" - the name still resolves and is logged as would-be-blocked. Nothing breaks. Use this first, and as the rollback.
  #   "BLOCK" - the name gets NXDOMAIN, so nothing can connect to it. Switch to this once the query logs
  #             (/aws/route53resolver/thor-dev) show ~7 days of normal traffic with no unexpected ALERT names
  #             and none from workflow or Lambda IPs.
  # egress_allowed_domains: tenant log vendor domains - "*.acme-logs.io" (all subdomains) or "ingest.acme-logs.io" (exact name).
  dns_firewall_default_action = "ALERT"
  egress_allowed_domains      = []

  # --- ecs compute ---
  # false until a real image has been pushed to each ECR repo below — the cluster/namespace/repos are created regardless.
  enable_compute            = true
  enable_container_insights = true

  # container_image = "" falls back to that service's own ECR repo at the "latest" tag; set it explicitly to pin a specific tag.
  services = {
    thor-api = {
      container_image       = ""
      container_port        = 8443
      cpu                   = 512  # 0.5 vCPU
      memory                = 1024 # 1 GB
      desired_count         = 2
      min_healthy_percent   = 100
      max_percent           = 200
      health_check_path     = "/health"
      log_retention_days    = 30
      environment_variables = {}
      secrets               = {}
      expose_via_nlb        = true
      nlb_listener_port     = 443
      deployment_strategy   = "BLUE_GREEN"
      bake_time_in_minutes  = 5
    }
    task-api = {
      container_image     = ""
      container_port      = 8443
      cpu                 = 512  # 0.5 vCPU
      memory              = 1024 # 1 GB
      desired_count       = 2
      min_healthy_percent = 100
      max_percent         = 200
      health_check_path   = "/health"
      log_retention_days  = 30
      environment_variables = {
        THOR_TASKAPI_SCAN_TASK_STALL_TIMEOUT_SECONDS = "1800"
        THOR_TASKAPI_SCAN_TASK_MAX_RETRIES           = "3"
      }
      secrets              = {}
      expose_via_nlb       = false
      deployment_strategy  = "BLUE_GREEN"
      bake_time_in_minutes = 5
    }
    intelligence-engine = {
      container_image       = ""
      container_port        = 8443
      cpu                   = 512  # 0.5 vCPU
      memory                = 1024 # 1 GB
      desired_count         = 2
      min_healthy_percent   = 100
      max_percent           = 200
      health_check_path     = "/health"
      log_retention_days    = 30
      environment_variables = {}
      secrets               = {}
      expose_via_nlb        = false
      deployment_strategy   = "BLUE_GREEN"
      bake_time_in_minutes  = 5
    }
  }

  # --- route53 + acm (hosted zones with their certificates nested) ---
  enable_route53 = true

  hosted_zones = {
    # Apex zone: no certs, exists only to hold thor's NS delegation record (parent_zone_name below).
    apex = {
      zone_name    = local.apex_domain
      certificates = {}
    }

    thor = {
      zone_name        = "dev.sphereboarddev.ai"
      parent_zone_name = local.apex_domain
      # modules/acm auto-routes each cert's validation record to the zone that serves its hostname.
      certificates = {
        # CloudFront (S3), us-east-1. include_wildcard covers tenant subdomains (tenant1.dev.sphereboarddev.ai).
        frontend = {
          domain_name      = "dev.sphereboarddev.ai"
          include_wildcard = true
        }
        # CloudFront (API GW), us-east-1. include_wildcard covers per-tenant API routing (tenant1.api.dev.sphereboarddev.ai).
        api = {
          domain_name      = "api.dev.sphereboarddev.ai"
          include_wildcard = true
        }
        # NLB TLS re-encryption (CN/SNI only). Only non-us-east-1 cert here — an NLB needs its cert
        # in-region, so scope = "regional" (resolves against var.aws_region in main.tf).
        backend = {
          domain_name = "backend.dev.sphereboarddev.ai"
          scope       = "regional"
        }
      }
    }
  }

  # --- frontend (static SPA: S3 + CloudFront) ---
  enable_frontend          = true
  frontend_price_class     = "PriceClass_100"
  frontend_certificate_key = "thor/frontend"

  # --- api cdn (CloudFront in front of the API Gateway REST API) ---
  api_cdn_certificate_key = "thor/api"
  api_cdn_price_class     = "PriceClass_100"
  api_cdn_waf_rate_limit  = 2000

  # --- nlb <-> ecs TLS re-encryption ---
  backend_certificate_key = "thor/backend"

  # --- database (Aurora PostgreSQL, task-api's) ---
  # Low capacity + no deletion protection — dev is throwaway, cost-optimized.
  aurora_database_name         = "thor_dev_db"
  aurora_master_username       = "thor_admin"
  aurora_engine_version        = "16.13"
  aurora_min_capacity          = 0.5
  aurora_max_capacity          = 1
  aurora_backup_retention_days = 7
  aurora_deletion_protection   = false
  aurora_skip_final_snapshot   = true

  # --- lambda authorizer ---
  authorizer_lambda_runtime     = "dotnet10"
  authorizer_lambda_timeout     = 60  # seconds
  authorizer_lambda_memory_size = 512 # MB
  # get_repo_root() stays valid across any Terragrunt cache copy. dotnet publish must have already written here.
  authorizer_source_dir = "${get_repo_root()}/backend/functions/Thor.Authorizer/publish"

  # --- workflows ---
  # Each name must have a matching entry in local.workflow_definitions (infra/src/workflow_definitions.tf),
  # which is where its steps, buckets and trigger live. The ECR repository and security group are
  # created regardless of enabled -- an image has to be pushable before the compute that runs it
  # exists, and rds_proxy/neptune build their ingress rules from the security group.
  #
  # Deploying a new image does NOT require a Terraform apply: Terraform points the compute at a
  # floating tag and CI moves that tag. enabled gates whether the workflow exists at all.
  workflows = {
    ingestion = { enabled = true }

    # Enabled alongside ingestion, which chains to it: ingestion's StartAtre state names ATRE's
    # state machine by built ARN, so leaving ATRE off would give the live ingestion definition a
    # final state pointing at nothing. ATRE is all Lambda, and Lambda resolves the image at
    # CreateFunction, so a first deploy needs the three steps in
    # docs/Infra_pipeline/workflow-deployment.md; dev is past that.
    atre = { enabled = true }

    # Chained from ingestion after ATRE (StartOwnership). All Lambda, and Lambda resolves the image
    # at CreateFunction, so enabling it needs CI to have pushed an ownership image first
    # (docs/Infra_pipeline/workflow-deployment.md, "First deployment of Ownership").
    ownership = { enabled = true }
  }

  # Lets Neptune's bulk loader read these workflows' graph-load buckets. Each is gated on its workflow
  # being enabled, so listing one that is still disabled grants nothing yet; any enabled one is also
  # what creates the loader role.
  #
  neptune_bulk_load_workflows = ["ingestion", "ownership"]

  create_manifest_source_dir = "${get_repo_root()}/backend/functions/Thor.CreateManifest/publish"

  # --- neptune graph db ---
  enable_neptune                = true
  neptune_engine_version        = "1.4.8.0"
  neptune_min_capacity          = 1
  neptune_max_capacity          = 8
  neptune_backup_retention_days = 7
  neptune_deletion_protection   = false
  neptune_skip_final_snapshot   = true

  # --- secret manager ---
  secrets_recovery_window_in_days = 0

  # Public half of the connector JWT keypair — committed, because it is public by design and the
  # authorizer Lambda needs it as a literal env var (see the root variable). Must stay identical
  # to the "public_key" field of thor-dev-secret-connector-jwt. Written by
  # scripts/seed-connector-secrets.sh; try() covers only the first apply on a new environment,
  # which creates the secret the script then seeds (infra/bootstrap/connector-secrets.md).
  connector_jwt_public_key = try(file("${get_terragrunt_dir()}/connector-jwt-public-key.pem"), "")

  # --- db bootstrap (in-VPC Lambda that mints the platform DB roles as master) ---
  db_bootstrap_source_dir = "${get_repo_root()}/backend/functions/Thor.DbBootstrap/publish"

  # TEMPORARY: thor-dev's master.__EFMigrationsHistory predates a Master migration squash and no
  # longer lines up with the committed migration IDs, so MigrateAsync tries to replay InitialCreate
  # against tables that already exist (42P07). No bastion/DB access to fix the history table
  # directly, so migrations are skipped here and the bootstrap only re-applies DB roles/grants.
  # thor-dev's schema is stale relative to the current model until this is reconciled — revert once
  # the history table is fixed.
  db_bootstrap_skip_migrations = false

  # --- master db seed (in-VPC Lambda that writes deployment seed data via IAM through the RDS Proxy) ---
  master_db_seed_source_dir = "${get_repo_root()}/backend/functions/Thor.MasterDbSeed/publish"
  master_seed_db_user       = "thor_master_seed"

  # --- tenant provisioning (Step Functions onboarding workflow) ---
  # Kept false until the DNS target below is real: with it on, apply creates the state machine,
  # six Lambdas, and (on execution) real Cognito/Route53 resources. `terraform validate` still
  # checks the module while it is off. Flip to true to plan/apply it.
  enable_tenant_provisioning     = true
  provisioning_db_user           = "thor_provisioner"
  master_db_app_user             = "thor_app"
  tenant_provisioning_source_dir = "${get_repo_root()}/backend/functions/Thor.TenantProvisioning/publish"

  # Tenant records go into the hosted_zones entry whose zone_name matches this.
  tenant_provisioning_base_domain = "dev.sphereboarddev.ai"
  # TODO: set to the API's public hostname (wildcard custom domain target) before enabling.
  tenant_provisioning_dns_target = "api.dev.sphereboarddev.ai"

  tags = {}
}