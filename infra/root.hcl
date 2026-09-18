locals {
  environment = basename(get_original_terragrunt_dir())

  account_map = {
    dev = {
      account_name = "dev" # Account A, shared with qa
      account_id   = "853973692277"
      aws_region   = "us-west-2"
      state_region = "us-east-1"
    }
    qa = {
      account_name = "qa" # Account A, shared with dev
      account_id   = "853973692277"
      aws_region   = "us-west-2"
      state_region = "us-east-1"
    }
    prod = {
      account_name = "prod" # Account B, isolated from dev/qa
      account_id   = ""
      aws_region   = "us-east-1"
    }
  }

  account = local.account_map[local.environment]

 ## jfrog_hostname = get_env("JFROG_HOSTNAME")
 ## repo_name      = get_env("JFROG_STATE_BACKEND_REPOSITORY")
}

# Backend config 
remote_state {
  backend = "s3"
  generate = {
    path      = "backend.tf"
    if_exists = "overwrite_terragrunt"
  }
  config = {
    bucket       = "thor-terraform-state-${local.account.account_id}"
    key          = "thor-${local.environment}/terraform.tfstate"
    region       = local.account.state_region
    use_lockfile = true
    encrypt      = true
  }
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
  account_id  = local.account.account_id
  aws_region  = local.account.aws_region
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
