terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

# One security group per workflow, owned here rather than inside modules/workflow.
#
# This is the move that makes splitting the estate into separate Terraform states possible at all.
# Before it, rds_proxy and neptune both read module.ingestion.task_security_group_id to build their
# ingress rules, so the platform depended on the workflow and the workflow depended on the platform
# (db_host, neptune_endpoint, the loader role). Inside one state Terraform resolves that; across a
# state boundary it is a genuine cycle, and no amount of terragrunt dependency wiring fixes it.
#
# Taking a plain list of workflow *names* breaks it. This module depends on nothing a workflow
# creates, so the platform state can build every ingress rule it needs without ever reading a
# workflow's state. The workflow units then depend one way, on the platform, which is acyclic.
#
# Deliberately not gated on whether the workflow is enabled: a security group with no member ENIs
# costs nothing, and gating it would reintroduce exactly the dependency this module exists to remove.

resource "aws_security_group" "workflow" {
  for_each = var.workflows

  name        = "thor-${var.environment}-${each.key}-task-sg"
  description = "Thor ${each.key} workflow compute - no inbound (invoked via the ECS/Lambda APIs, not network traffic), egress open (no NAT/IGW route, so this only reaches the VPC + endpoints in practice)"
  vpc_id      = var.vpc_id

  egress {
    description = "All traffic"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(var.tags, {
    Name = "thor-${var.environment}-${each.key}-task-sg"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
