include "root" {
  path = find_in_parent_folders("root.hcl")
}

terraform {
  source = "../../src"
}

# Every value the root module accepts is spelled out below instead of relying on a default in infra/src/variables.tf; only `environment` is left out, since infra/root.hcl already supplies it for every environment.
inputs = {
  # --- network ---
  enable_network       = true
  vpc_cidr             = "10.0.0.0/16"
  az_count             = 2
  public_subnet_cidrs  = ["10.0.0.0/24", "10.0.1.0/24"]
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
      container_port        = 8080
      cpu                   = 512 # 0.5 vCPU
      memory                = 1024 # 1 GB
      desired_count         = 2
      min_healthy_percent   = 100
      max_percent           = 200
      health_check_path     = "/health"
      log_retention_days    = 30
      environment_variables = {}
      secrets               = {}
      expose_publicly       = true
      nlb_listener_port     = 80
      deployment_strategy   = "BLUE_GREEN"
      bake_time_in_minutes  = 5
    }
    task-api = {
      container_image       = ""
      container_port        = 8080
      cpu                   = 512  # 0.5 vCPU
      memory                = 1024 # 1 GB
      desired_count         = 2
      min_healthy_percent   = 100
      max_percent           = 200
      health_check_path     = "/health"
      log_retention_days    = 30
      environment_variables = {}
      secrets               = {}
      expose_publicly       = false
      deployment_strategy   = "BLUE_GREEN"
      bake_time_in_minutes  = 5
    }
    intelligence-engine = {
      container_image       = ""
      container_port        = 8080
      cpu                   = 512  # 0.5 vCPU
      memory                = 1024 # 1 GB
      desired_count         = 2
      min_healthy_percent   = 100
      max_percent           = 200
      health_check_path     = "/health"
      log_retention_days    = 30
      environment_variables = {}
      secrets               = {}
      expose_publicly       = false
      deployment_strategy   = "BLUE_GREEN"
      bake_time_in_minutes  = 5
    }
  }

  # --- frontend (static SPA: S3 + CloudFront) ---
  enable_frontend      = true
  frontend_price_class = "PriceClass_100"

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

  # --- db bootstrap (in-VPC Lambda that mints the platform DB roles as master) ---
  db_bootstrap_source_dir = "${get_repo_root()}/backend/functions/Thor.DbBootstrap/publish"

  # --- tenant provisioning (Step Functions onboarding workflow) ---
  # Kept false until the Route53 hosted zone + domain values below are real: with it on, apply
  # creates the state machine, six Lambdas, and (on execution) real Cognito/Route53 resources.
  # `terraform validate` still checks the module while it is off. Flip to true to plan/apply it.
  enable_tenant_provisioning     = false
  provisioning_db_user           = "thor_provisioner"
  master_db_app_user             = "thor_app"
  tenant_provisioning_source_dir = "${get_repo_root()}/backend/functions/Thor.TenantProvisioning/publish"

  # TODO: replace with the real dev hosted zone / domain before enabling.
  tenant_provisioning_hosted_zone_id = ""
  tenant_provisioning_base_domain    = "tenants.dev.example.com"
  tenant_provisioning_dns_target     = ""

  tags = {}
}