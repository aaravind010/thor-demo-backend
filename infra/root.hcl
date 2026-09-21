locals {
  environment = basename(get_original_terragrunt_dir())

  account_map = {
    dev = {
      account_name = "dev" # Account A, shared with qa
      account_id   = get_aws_account_id()
      aws_region   = get_env("${upper(local.environment)}_AWS_REGION")
    }
    qa = {
      account_name = "qa" # Account A, shared with dev
      account_id   = get_aws_account_id()
      aws_region   = get_env("${upper(local.environment)}_AWS_REGION")
    }
    prod = {
      account_name = "prod" # Account B, isolated from dev/qa
      account_id   = get_aws_account_id()
      aws_region   = get_env("${upper(local.environment)}_AWS_REGION")
    }
  }

  account = local.account_map[local.environment]

  jfrog_hostname = get_env("JFROG_HOSTNAME")
  repo_name      = get_env("JFROG_STATE_BACKEND_REPOSITORY")
}

# Backend config via generate
generate "backend" {
  path      = "backend.tf"
  if_exists = "overwrite_terragrunt"
  contents  = <<EOF
terraform {
  backend "remote" {
    hostname     = "${local.jfrog_hostname}"
    organization = "${local.repo_name}"

    workspaces {
      name = "thor-${local.environment}-${local.account.aws_region}"
    }
  }
}
EOF
}

generate "provider" {
  path      = "provider.tf"
  if_exists = "overwrite_terragrunt"
  contents  = <<EOF
terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

provider "aws" {
  region = "${local.account.aws_region}"


  default_tags {
    tags = {
      Project     = "thor-platform"
      Environment = "${local.environment}"
      ManagedBy   = "terragrunt"
    }
  }
}
EOF
}

inputs = {
  environment = local.environment
  aws_region  = local.account.aws_region
  account_id  = local.account.account_id
}

# dotnet publish before plan/apply/destroy, so archive_file has real code to zip. Skippable via SKIP_LAMBDA_PUBLISH=true — used by CI's apply job, which already has the zip and doesn't need a rebuild.
terraform {
  before_hook "publish_lambda_functions" {
    commands     = ["plan", "apply", "destroy"]
    execute      = ["${get_repo_root()}/scripts/publish-lambda-functions.sh"]
    run_on_error = false
    if           = get_env("SKIP_LAMBDA_PUBLISH", "false") != "true"
  }
}
