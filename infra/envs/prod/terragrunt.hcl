include "root" {
  path = find_in_parent_folders("root.hcl")
}

terraform {
  source = "../../src"
}

locals {
  apex_domain = "sphereboard.ai"
}

# Every value the root module accepts is spelled out below instead of relying on a default in infra/src/variables.tf; only `environment` is left out, since infra/root.hcl already supplies it for every environment.
inputs = {
  # --- network ---
  vpc_cidr             = "10.0.0.0/16"
  az_count             = 2
  private_subnet_cidrs = ["10.0.10.0/24", "10.0.11.0/24"]
  enable_vpc_endpoints = true

  # --- ecs compute ---
  # false until a real image has been pushed to each ECR repo below — the cluster/namespace/repos are created regardless.
  enable_compute            = false
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
    thor = {
      zone_name = local.apex_domain
      certificates = {
        # CloudFront (S3), us-east-1. include_wildcard covers tenant subdomains (tenant1.sphereboard.ai).
        frontend = {
          domain_name      = local.apex_domain
          include_wildcard = true
        }
        # CloudFront (API GW), us-east-1. include_wildcard covers per-tenant API routing (tenant1.api.sphereboard.ai).
        api = {
          domain_name      = "api.${local.apex_domain}"
          include_wildcard = true
        }
        # NLB TLS re-encryption (CN/SNI only). Only non-us-east-1 cert here — an NLB needs its cert
        # in-region, so scope = "regional" (resolves against var.aws_region in main.tf).
        backend = {
          domain_name = "backend.${local.apex_domain}"
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
  # Low capacity + no deletion protection — prod is throwaway, cost-optimized.
  aurora_database_name         = "thor_prod_db"
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

  # --- ingestion pipeline ---
  enable_ingestion           = false
  create_manifest_source_dir = "${get_repo_root()}/backend/functions/Thor.CreateManifest/publish"

  # --- neptune graph db ---
  enable_neptune                = true
  neptune_engine_version        = "1.4.8.0"
  neptune_min_capacity          = 2
  neptune_max_capacity          = 16
  neptune_backup_retention_days = 30
  neptune_deletion_protection   = true
  neptune_skip_final_snapshot   = false

  # --- secret manager ---
  secrets_recovery_window_in_days = 30

  tags = {}
}