# One-shot Fargate task Step Functions runs on demand (ecs:runTask.sync) — inspect once per
# execution, apply once per changed tenant. Own cluster, no aws_ecs_service. A single container:
# it only inspects and applies; diffing (which needs a Postgres dev database) happens in CI.

resource "aws_ecs_cluster" "runner_cluster" {
  name = local.name_prefix

  setting {
    name  = "containerInsights"
    value = var.enable_container_insights ? "enabled" : "disabled"
  }

  tags = merge(var.tags, {
    Name = local.name_prefix
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_security_group" "runner_task_sg" {
  name        = "${local.name_prefix}-task-sg"
  description = "Tenant migration ECS task - no inbound (invoked via the ECS API), egress open (no NAT/IGW route, so this only reaches the VPC + endpoints in practice)"
  vpc_id      = var.vpc_id

  egress {
    description = "All traffic"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-task-sg"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_cloudwatch_log_group" "runner_log_group" {
  name              = "/ecs/${var.environment}/${local.name_prefix}"
  retention_in_days = var.log_retention_days
  tags              = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

data "aws_iam_policy_document" "ecs_task_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "runner_execution_role" {
  name                 = "${local.name_prefix}-execution"
  assume_role_policy   = data.aws_iam_policy_document.ecs_task_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_iam_role_policy_attachment" "runner_execution_policy_attachment" {
  role       = aws_iam_role.runner_execution_role.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role" "runner_task_role" {
  name                 = "${local.name_prefix}-task"
  assume_role_policy   = data.aws_iam_policy_document.ecs_task_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Container's own runtime permissions — add statements here as needed, not a new policy resource.
data "aws_iam_policy_document" "runner_task_permissions_document" {
  # Writes live-schema snapshots, results and per-tenant state; reads approved plans.
  statement {
    actions   = ["s3:GetObject", "s3:PutObject"]
    resources = ["${aws_s3_bucket.migration_bucket.arn}/*"]
  }

  statement {
    actions = ["rds-db:connect"]
    resources = [
      "${local.rds_db_arn_prefix}/${var.master_db_app_user}",
      "${local.rds_db_arn_prefix}/tenant_*_rw",
    ]
  }
}

resource "aws_iam_role_policy" "runner_task_permissions" {
  name   = "${local.name_prefix}-task-permissions"
  role   = aws_iam_role.runner_task_role.id
  policy = data.aws_iam_policy_document.runner_task_permissions_document.json
}

# Terraform owns the task definition's roles, env and sizing. The image is a placeholder:
# tenant-migrations.yml registers a new revision of this family with the image it just built and
# passes that revision's ARN into the execution, so Terraform never needs to know the image tag.
resource "aws_ecs_task_definition" "runner_task_definition" {
  family                   = local.name_prefix
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.task_cpu
  memory                   = var.task_memory
  execution_role_arn       = aws_iam_role.runner_execution_role.arn
  task_role_arn            = aws_iam_role.runner_task_role.arn

  container_definitions = jsonencode([
    {
      name      = local.container_name
      image     = "${aws_ecr_repository.runner_ecr.repository_url}:latest"
      essential = true

      # No secrets block: every DB leg authenticates with an RDS IAM token the task mints itself.
      # THOR_MODE and THOR_INPUT are supplied per invocation by the state machine.
      environment = [
        { name = "THOR_MIGRATION_BUCKET", value = aws_s3_bucket.migration_bucket.bucket },
        { name = "THOR_AWS_REGION", value = var.aws_region },
        { name = "AWS_REGION", value = var.aws_region },
        { name = "THOR_MASTERDB_HOST", value = var.db_host },
        { name = "THOR_MASTERDB_DATABASE", value = var.db_name },
        { name = "THOR_MASTERDB_USER", value = var.master_db_app_user },
        { name = "THOR_DB_PORT", value = "5432" },
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.runner_log_group.name
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = "runner"
        }
      }
    }
  ])

  tags = var.tags

  depends_on = [
    aws_iam_role_policy_attachment.runner_execution_policy_attachment,
    aws_iam_role_policy.runner_task_permissions,
  ]

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
