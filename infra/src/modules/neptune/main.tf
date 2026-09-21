terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

locals {
  name_prefix = "thor-${var.environment}-neptune"
}

resource "aws_neptune_subnet_group" "neptune" {
  name       = local.name_prefix
  subnet_ids = var.private_subnet_ids

  tags = merge(var.tags, {
    Name = local.name_prefix
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# No inline ingress — separate aws_vpc_security_group_ingress_rule resources below, same
# dependency-cycle reasoning as modules/aurora and modules/ecs (an SG's own inline ingress block
# referencing another module's SG would force those modules to depend on each other's SG output
# before either SG exists).
resource "aws_security_group" "neptune" {
  name        = "${local.name_prefix}-sg"
  description = "Neptune cluster - ingress per consumer_security_group_ids on 8182 (Gremlin), egress scoped to the VPC"
  vpc_id      = var.vpc_id

  egress {
    description = "Within VPC only"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = [var.vpc_cidr]
  }

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-sg"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# create_ingress is its own explicit flag (wired to enable_compute at the root), not inferred from
# consumer_security_group_ids being empty — an empty map is ambiguous (compute intentionally off
# vs. a bug in how the caller built the map), whereas the root module always knows definitively
# whether its ECS consumers exist. One rule per map entry; map keys (not the SG IDs) drive for_each
# so this still works when an ID is only known after apply (see modules/aurora's identical pattern).
resource "aws_vpc_security_group_ingress_rule" "neptune" {
  for_each = var.create_ingress ? var.consumer_security_group_ids : {}

  security_group_id            = aws_security_group.neptune.id
  description                  = "Gremlin from ${each.key}"
  referenced_security_group_id = each.value
  from_port                    = 8182
  to_port                      = 8182
  ip_protocol                  = "tcp"

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-${each.key}-ingress"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# iam_database_authentication_enabled, no master-password secret — Neptune uses SigV4/IAM DB auth
# instead of Aurora's manage_master_user_password, so consumers authenticate via their own IAM
# role/policy against the neptune-db:* actions, not a Secrets-Manager credential.
resource "aws_neptune_cluster" "neptune" {
  cluster_identifier                  = local.name_prefix
  engine                              = "neptune"
  engine_version                      = var.engine_version
  neptune_subnet_group_name           = aws_neptune_subnet_group.neptune.name
  vpc_security_group_ids              = [aws_security_group.neptune.id]
  iam_database_authentication_enabled = true
  storage_encrypted                   = true

  backup_retention_period   = var.backup_retention_days
  deletion_protection       = var.deletion_protection
  skip_final_snapshot       = var.skip_final_snapshot
  final_snapshot_identifier = var.skip_final_snapshot ? null : "${local.name_prefix}-final"

  # Underscore before v2 here — unlike aws_rds_cluster's serverlessv2_scaling_configuration (see
  # modules/aurora/main.tf's comment). A real hashicorp/aws provider-schema inconsistency between
  # the two resource types, not something to "fix" on either side.
  serverless_v2_scaling_configuration {
    min_capacity = var.min_capacity
    max_capacity = var.max_capacity
  }

  tags = merge(var.tags, {
    Name = local.name_prefix
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Serverless v2 still needs one instance resource — this is what actually runs; the cluster above
# is storage/control only (same split as modules/aurora). Identifier must stay under
# "thor-<environment>-neptune-*" — the CI/CD deploy role's Neptune IAM statement
# (docs/infra_pipeline_setup_guide.md) is scoped to exactly that wildcard.
resource "aws_neptune_cluster_instance" "neptune" {
  identifier         = "${local.name_prefix}-instance"
  cluster_identifier = aws_neptune_cluster.neptune.id
  instance_class     = "db.serverless"
  engine             = aws_neptune_cluster.neptune.engine
  engine_version     = aws_neptune_cluster.neptune.engine_version

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-instance"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Second instance for Multi-AZ failover — kept as its own resource, not for_each, so it's purely
# additive (same pattern as modules/aurora's aurora_instance_2). Storage itself already replicates
# across 3 AZs regardless of instance count; this is what gives fast compute-level failover instead
# of needing a brand-new instance provisioned after the primary goes down.
resource "aws_neptune_cluster_instance" "neptune_2" {
  identifier         = "${local.name_prefix}-instance-2"
  cluster_identifier = aws_neptune_cluster.neptune.id
  instance_class     = "db.serverless"
  engine             = aws_neptune_cluster.neptune.engine
  engine_version     = aws_neptune_cluster.neptune.engine_version

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-instance-2"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
