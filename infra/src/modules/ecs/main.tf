terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

data "aws_region" "current" {}

# Gates services.tf/nlb.tf/security_groups.tf/iam.tf — this cluster, the namespace below, and the ECR repos in ecr.tf stay unconditional since they don't need a container image to exist.
locals {
  active_services = var.enable_compute ? var.services : {}
}

resource "aws_ecs_cluster" "thor-ecs-cluster" {
  name = "thor-${var.environment}"

  setting {
    name  = "containerInsights"
    value = var.enable_container_insights ? "enabled" : "disabled"
  }

  tags = merge(var.tags, {
    Name = "thor-${var.environment}-cluster"
  })

  # This org's Cloud Custodian auto-tags resources (Owner/c7n-created-*) after creation, and an SCP blocks ecs:UntagResource from stripping them back off, so Terraform must ignore tags here instead of fighting Custodian every apply.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# HTTP namespace, not Route53 private-DNS — Service Connect's Envoy sidecar resolves traffic itself and shares this namespace across every service so they can reach each other by name.
resource "aws_service_discovery_http_namespace" "thor-sc-namespace" {
  name        = "thor-${var.environment}.local"
  description = "Service Connect namespace for thor-${var.environment}"
  tags        = var.tags
}
