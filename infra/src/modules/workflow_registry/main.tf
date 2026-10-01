terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

# One ECR repository per workflow, created unconditionally — a repo must exist before a workflow's
# `enabled` can turn on, and CI pushes the image before Terraform is ever told about the tag.
#
# This is deliberately its own module rather than part of modules/workflow. The workflow units are
# split into their own Terraform states (infra/envs/<env>/workflows/<name>/), and a per-workflow
# state cannot own the repo it bootstraps from: CI has to push :<sha> before the unit that consumes
# the tag can apply. Keeping the registry in the platform state breaks that ordering problem.
#
# Naming keeps the pre-split "thor-<env>-<name>-ecr" shape rather than adopting the "workflow-"
# infix the buckets use. ECR has no rename — a new name is a destroy and re-create of the one
# already-applied, potentially image-bearing resource in this refactor, in exchange for nothing but
# tidier naming. The root's moved block re-addresses it in state without touching the repo itself.

resource "aws_ecr_repository" "workflow" {
  for_each = var.workflows

  name = "thor-${var.environment}-${each.key}-ecr"

  # Immutable for everything except the floating deploy tag. Builds stay pinned to their digest and
  # cannot be overwritten, which is the point of immutability; the one tag CI moves on each deploy is
  # excluded, which is what lets an image deploy avoid Terraform entirely (see modules/workflow's
  # image_tag). Plain IMMUTABLE would make the floating tag pushable exactly once.
  image_tag_mutability = "IMMUTABLE_WITH_EXCLUSION"

  image_tag_mutability_exclusion_filter {
    filter      = var.deploy_tag
    filter_type = "WILDCARD"
  }

  image_scanning_configuration {
    scan_on_push = true
  }

  encryption_configuration {
    encryption_type = "AES256"
  }

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_ecr_lifecycle_policy" "workflow" {
  for_each = var.workflows

  repository = aws_ecr_repository.workflow[each.key].name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Expire untagged images after 7 days"
        selection = {
          tagStatus   = "untagged"
          countType   = "sinceImagePushed"
          countUnit   = "days"
          countNumber = 7
        }
        action = { type = "expire" }
      },
      {
        rulePriority = 2
        description  = "Keep only the last 20 tagged images"
        selection = {
          tagStatus      = "tagged"
          tagPatternList = ["*"]
          countType      = "imageCountMoreThan"
          countNumber    = 20
        }
        action = { type = "expire" }
      }
    ]
  })
}
