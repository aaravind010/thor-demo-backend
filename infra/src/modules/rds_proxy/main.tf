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
  name_prefix = "thor-${var.environment}-rds-proxy"
}

# No inline ingress — separate rule below, same reasoning as the aurora module.
resource "aws_security_group" "rds_proxy_security_group" {
  name        = "${local.name_prefix}-sg"
  description = "RDS Proxy - ingress per allowed_security_group_ids, egress scoped to the VPC"
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

# One rule per entry in allowed_security_group_ids — same pattern as the aurora module's own ingress rule.
resource "aws_vpc_security_group_ingress_rule" "rds_proxy_security_group_ingress" {
  for_each = var.allowed_security_group_ids

  security_group_id            = aws_security_group.rds_proxy_security_group.id
  description                  = "PostgreSQL from ${each.key}"
  referenced_security_group_id = each.value
  from_port                    = 5432
  to_port                      = 5432
  ip_protocol                  = "tcp"

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-${each.key}-ingress"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

data "aws_iam_policy_document" "rds_proxy_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["rds.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "rds_proxy_role" {
  name                 = "${local.name_prefix}-role"
  assume_role_policy   = data.aws_iam_policy_document.rds_proxy_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# The proxy's own permissions — add more statements here as it needs more, not a new aws_iam_role_policy per permission.
data "aws_iam_policy_document" "rds_proxy_permissions" {
  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [var.aurora_secret_arn]
  }
}

resource "aws_iam_role_policy" "rds_proxy_permissions" {
  name   = "rds-proxy-permissions"
  role   = aws_iam_role.rds_proxy_role.id
  policy = data.aws_iam_policy_document.rds_proxy_permissions.json
}

resource "aws_db_proxy" "rds_proxy" {
  name                   = local.name_prefix
  engine_family          = "POSTGRESQL"
  role_arn               = aws_iam_role.rds_proxy_role.arn
  vpc_subnet_ids         = var.private_subnet_ids
  vpc_security_group_ids = [aws_security_group.rds_proxy_security_group.id]
  require_tls            = true

  auth {
    auth_scheme = "SECRETS"
    secret_arn  = var.aurora_secret_arn
    iam_auth    = "DISABLED"
  }

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_db_proxy_default_target_group" "rds_proxy_target_group" {
  db_proxy_name = aws_db_proxy.rds_proxy.name

  connection_pool_config {
    max_connections_percent = 100
  }
}

resource "aws_db_proxy_target" "rds_proxy_target" {
  db_proxy_name         = aws_db_proxy.rds_proxy.name
  target_group_name     = aws_db_proxy_default_target_group.rds_proxy_target_group.name
  db_cluster_identifier = var.aurora_cluster_identifier
}
