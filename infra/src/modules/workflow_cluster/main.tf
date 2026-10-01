terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

# One ECS cluster shared by every workflow, replacing the per-workflow cluster modules/ingestion
# created. A cluster is a namespace and a capacity-provider binding, not an isolation boundary —
# isolation between workflows comes from their separate task roles and security groups, which they
# keep. One cluster per workflow would multiply Container Insights cost by N for no separation.
#
# Lives in the platform state with the registry and the security groups: it is created before any
# workflow is enabled, and a workflow unit only reads its name.
#
# No aws_ecs_service anywhere — workflow tasks are one-shot, run on demand by Step Functions via
# ecs:runTask.sync.

resource "aws_ecs_cluster" "workflows" {
  name = "thor-${var.environment}-workflows"

  setting {
    name  = "containerInsights"
    value = var.enable_container_insights ? "enabled" : "disabled"
  }

  tags = merge(var.tags, {
    Name = "thor-${var.environment}-workflows"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
