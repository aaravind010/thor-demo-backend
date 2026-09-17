include "root" {
  path = find_in_parent_folders("root.hcl")
}

terraform {
  source = "../../src"
}

locals {
  root_domain = "sphereboard.ai"
}

# Every value the root module accepts is spelled out below instead of relying on a default in infra/src/variables.tf — same convention as envs/dev/terragrunt.hcl; only `environment` is left out, since infra/root.hcl already supplies it for every environment.
inputs = {
  # --- network ---
  vpc_cidr             = "10.0.0.0/16"
  az_count             = 2
  public_subnet_cidrs  = ["10.0.0.0/24", "10.0.1.0/24"]
  private_subnet_cidrs = ["10.0.10.0/24", "10.0.11.0/24"]
  enable_vpc_endpoints = true

  # --- ecs compute ---
  enable_compute            = false
  enable_container_insights = true

  # container_image = "" falls back to that service's own ECR repo at the "latest" tag; set it explicitly once CI promotes a real image. BLUE_GREEN for all services, including thor-api, validated via the NLB test-traffic listener (nlb.tf) before cutover.
  services = {
    thor-api = {
      container_image       = ""
      container_port        = 8443
      cpu                   = 512  # 0.5 vCPU
      memory                = 1024  # 1 GB
      desired_count         = 4
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
      desired_count         = 4
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
      desired_count         = 4
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
  # Own domain, own AWS account — no shared apex to delegate from, so unlike dev/qa there's just the one zone.
  enable_route53 = true

  hosted_zones = {
    thor = {
      zone_name = local.root_domain
      certificates = {
        frontend = {
          domain_name = local.root_domain
        }
        api_gateway = {
          domain_name = "api.${local.root_domain}"
        }
        # NLB's TLS listener cert (re-encryption) — CN/SNI only, no DNS record needed.
        backend = {
          domain_name = "backend.${local.root_domain}"
        }
      }
    }
  }

  # --- frontend (static SPA: S3 + CloudFront) ---
  enable_frontend          = true
  frontend_price_class     = "PriceClass_100"
  frontend_certificate_key = "thor/frontend"

  # --- api gateway custom domain ---
  api_gateway_certificate_key = "thor/api_gateway"

  # --- nlb <-> ecs TLS re-encryption ---
  backend_certificate_key = "thor/backend"

  # --- database (Aurora PostgreSQL, task-api's) ---
  # Deletion protection on, real final snapshot kept, longer backup retention than dev — prod is not disposable. min/max_capacity are a starting guess, not tuned against real traffic yet.
  aurora_database_name         = "thor_prod_db"
  aurora_master_username       = "thor_admin"
  aurora_engine_version        = "16.13"
  aurora_min_capacity          = 1
  aurora_max_capacity          = 4
  aurora_backup_retention_days = 30
  aurora_deletion_protection   = true
  aurora_skip_final_snapshot   = false

  # --- lambda authorizer ---
  authorizer_lambda_runtime     = "dotnet10"
  authorizer_lambda_timeout     = 60  # seconds
  authorizer_lambda_memory_size = 512 # MB
  # get_repo_root() stays valid across any Terragrunt cache copy. dotnet publish must have already written here.
  authorizer_source_dir = "${get_repo_root()}/backend/functions/Thor.Authorizer/publish"

  # --- ingestion pipeline ---
  enable_ingestion           = false
  create_manifest_source_dir = "${get_repo_root()}/backend/functions/CreateManifest/publish"

  tags = {}
}
