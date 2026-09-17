include "root" {
  path = find_in_parent_folders("root.hcl")
}

terraform {
  source = "../../src"
}

locals {
  root_domain = "sphereboarddev.ai"
}

# Every input is spelled out explicitly, same as envs/dev/terragrunt.hcl; `environment` is omitted — infra/root.hcl supplies it.
inputs = {
  # --- network ---
  # Own VPC (not borrowed from dev) — 10.1.0.0/16 avoids colliding with dev/prod's 10.0.0.0/16 if ever peered.
  vpc_cidr             = "10.1.0.0/16"
  az_count             = 2
  public_subnet_cidrs  = ["10.1.0.0/24", "10.1.1.0/24"]
  private_subnet_cidrs = ["10.1.10.0/24", "10.1.11.0/24"]
  enable_vpc_endpoints = true

  # --- ecs compute ---
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
  # apex is looked up, not created (dev already owns sphereboarddev.ai) — qa only creates its own "qa." subdomain zone.
  enable_route53 = true

  hosted_zones = {
    apex = {
      zone_name    = local.root_domain
      create_zone  = false
      certificates = {}
    }

    thor = {
      zone_name = "qa.${local.root_domain}"
      certificates = {
        frontend = {
          domain_name = "qa.${local.root_domain}"
        }
        api_gateway = {
          domain_name = "api.qa.${local.root_domain}"
        }
        # NLB's TLS listener cert (re-encryption) — CN/SNI only, no DNS record needed.
        backend = {
          domain_name = "backend.qa.${local.root_domain}"
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
