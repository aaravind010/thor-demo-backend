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
  name_prefix = "thor-${var.environment}-aurora"

  # Falls back to a per-environment Postgres database name when left blank — underscores, not hyphens, since unquoted Postgres identifiers can't contain them.
  database_name = var.database_name != "" ? var.database_name : "thor_${var.environment}_db"
}

resource "aws_db_subnet_group" "aurora_subnet_group" {
  name       = local.name_prefix
  subnet_ids = var.private_subnet_ids

  tags = merge(var.tags, {
    Name = local.name_prefix
  })
}

# No inline ingress — kept in a separate aws_vpc_security_group_ingress_rule below, same reasoning as modules/ecs/security_groups.tf (avoids the same Terraform dependency-cycle risk).
resource "aws_security_group" "aurora_security_group" {
  name        = "${local.name_prefix}-sg"
  description = "Aurora PostgreSQL - ingress from task-api only, egress scoped to the VPC"
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
}

# Gated on create_task_api_ingress (a plain bool, = var.enable_compute), not a null-check on task_api_security_group_id — that value can be unknown mid-apply (still-changing security group), which breaks count's plan-time-known requirement; a plain bool doesn't.
resource "aws_vpc_security_group_ingress_rule" "task_api" {
  count = var.create_task_api_ingress ? 1 : 0

  security_group_id            = aws_security_group.aurora_security_group.id
  description                  = "PostgreSQL from task-api"
  referenced_security_group_id = var.task_api_security_group_id
  from_port                    = 5432
  to_port                      = 5432
  ip_protocol                  = "tcp"

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-task-api-ingress"
  })
}

# manage_master_user_password: AWS-generated/rotated in Secrets Manager, never touches Terraform state. enable_http_endpoint: RDS Data API, so a GitHub-hosted CI runner can reach this without VPC network access.
resource "aws_rds_cluster" "aurora_cluster" {
  cluster_identifier     = local.name_prefix
  engine                 = "aurora-postgresql"
  engine_version         = var.engine_version
  database_name          = local.database_name
  master_username        = var.master_username
  db_subnet_group_name   = aws_db_subnet_group.aurora_subnet_group.name
  vpc_security_group_ids = [aws_security_group.aurora_security_group.id]

  manage_master_user_password = true
  storage_encrypted           = true
  enable_http_endpoint        = true

  backup_retention_period   = var.backup_retention_days
  deletion_protection       = var.deletion_protection
  skip_final_snapshot       = var.skip_final_snapshot
  final_snapshot_identifier = var.skip_final_snapshot ? null : "${local.name_prefix}-final"

  serverlessv2_scaling_configuration {
    min_capacity = var.min_capacity
    max_capacity = var.max_capacity
  }

  tags = merge(var.tags, {
    Name = local.name_prefix
  })
}

# Serverless v2 still requires at least one instance resource even though capacity itself is elastic — this is what actually runs, the cluster above is just the control plane/storage layer.
resource "aws_rds_cluster_instance" "aurora_instance" {
  identifier         = "${local.name_prefix}-instance"
  cluster_identifier = aws_rds_cluster.aurora_cluster.id
  instance_class     = "db.serverless"
  engine             = aws_rds_cluster.aurora_cluster.engine
  engine_version     = aws_rds_cluster.aurora_cluster.engine_version

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-instance"
  })
}
