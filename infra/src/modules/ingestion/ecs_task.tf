# One-shot Fargate task Step Functions runs on demand (ecs:runTask.sync, state_machine.tf), on
# its own dedicated cluster isolated from modules/ecs's shared cluster. No aws_ecs_service — this
# isn't a continuously-running service.

resource "aws_ecs_cluster" "ecs_ingestion_cluster" {
  count = local.ingestion_active ? 1 : 0

  name = "${local.name_prefix}-cluster"

  setting {
    name  = "containerInsights"
    value = var.enable_container_insights ? "enabled" : "disabled"
  }

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-cluster"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_security_group" "ingestion_task_sg" {
  count = local.ingestion_active ? 1 : 0

  name        = "${local.name_prefix}-task-sg"
  description = "Ingestion ECS task - no inbound (invoked via the ECS API, not network traffic), egress open (no NAT/IGW route, so this only reaches the VPC + endpoints in practice)"
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

resource "aws_cloudwatch_log_group" "ingestion_task_log_group" {
  count = local.ingestion_active ? 1 : 0

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

resource "aws_iam_role" "ingestion_execution_role" {
  count = local.ingestion_active ? 1 : 0

  name                 = "${local.name_prefix}-execution"
  assume_role_policy   = data.aws_iam_policy_document.ecs_task_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_iam_role_policy_attachment" "ingestion_execution_policy_attachment" {
  count = local.ingestion_active ? 1 : 0

  role       = aws_iam_role.ingestion_execution_role[0].name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role" "ingestion_task_role" {
  count = local.ingestion_active ? 1 : 0

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
# Also the base of the per-step Lambda roles (lambda_steps.tf, via source_policy_documents), so the
# two compute targets can't drift apart in what the step code is allowed to do.
data "aws_iam_policy_document" "ingestion_task_permissions_document" {
  count = local.ingestion_active ? 1 : 0

  statement {
    actions   = ["s3:GetObject", "s3:ListBucket"]
    resources = [aws_s3_bucket.s3_ingestion[0].arn, "${aws_s3_bucket.s3_ingestion[0].arn}/*"]
  }

  # Master-DB routing lookup plus the tenant DB the step actually writes, both through the proxy.
  statement {
    actions   = ["rds-db:connect"]
    resources = local.rds_db_connect_resources
  }

  # Broad read on every thor-<environment>-* secret (AD/CyberArk/Windows connector credentials) —
  # the task picks which one it needs at runtime, so it can't be scoped to a fixed ARN. On the task
  # role, not the execution role: this is the container's own SDK call, not a secrets-block injection.
  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = ["arn:aws:secretsmanager:${var.aws_region}:${var.account_id}:secret:thor-${var.environment}-*"]
  }

  # Loader actions for graph-load-start/graph-load-poll; query actions for any stage querying the
  # graph directly. Skipped when Neptune's off (neptune_cluster_resource_id == "") — an empty
  # resource id isn't a valid neptune-db ARN.
  dynamic "statement" {
    for_each = var.neptune_cluster_resource_id != "" ? [1] : []

    content {
      actions = [
        "neptune-db:StartLoaderJob",
        "neptune-db:GetLoaderJobStatus",
        "neptune-db:CancelLoaderJob",
        "neptune-db:ReadDataViaQuery",
        "neptune-db:WriteDataViaQuery",
        "neptune-db:GetQueryStatus",
      ]
      resources = ["arn:aws:neptune-db:${var.aws_region}:${var.account_id}:${var.neptune_cluster_resource_id}/*"]
    }
  }
}

resource "aws_iam_role_policy" "ingestion_task_permissions" {
  count = local.ingestion_active ? 1 : 0

  name   = "${local.name_prefix}-task-permissions"
  role   = aws_iam_role.ingestion_task_role[0].id
  policy = data.aws_iam_policy_document.ingestion_task_permissions_document[0].json
}

# Second task role for local.graph_load_steps only. IAM can't tell task definitions apart on a
# shared role (no condition key carries the task family), so the write grant needs its own role —
# extract-stage/promote stay on ingestion_task_role and never get s3:PutObject.
resource "aws_iam_role" "ingestion_graph_load_task_role" {
  count = local.ingestion_active ? 1 : 0

  name                 = "${local.name_prefix}-graph-load-task"
  assume_role_policy   = data.aws_iam_policy_document.ecs_task_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Everything ingestion_task_role has (inherited via source_policy_documents, so additions to the base
# document reach both roles) plus the bucket write the graph-load steps need.
data "aws_iam_policy_document" "ingestion_graph_load_task_permissions_document" {
  count = local.ingestion_active ? 1 : 0

  source_policy_documents = [data.aws_iam_policy_document.ingestion_task_permissions_document[0].json]

  statement {
    actions   = ["s3:PutObject"]
    resources = ["${aws_s3_bucket.s3_ingestion[0].arn}/*"]
  }
}

resource "aws_iam_role_policy" "ingestion_graph_load_task_permissions" {
  count = local.ingestion_active ? 1 : 0

  name   = "${local.name_prefix}-graph-load-task-permissions"
  role   = aws_iam_role.ingestion_graph_load_task_role[0].id
  policy = data.aws_iam_policy_document.ingestion_graph_load_task_permissions_document[0].json
}

# One task definition per THOR_STEP, all from the same image (Thor.Workflows.Ingestion is one
# Docker image, four entry points) — baking THOR_STEP into the task def (not just a per-invocation
# override) lets any stage be run standalone via ecs:RunTask without Step Functions. THOR_INPUT is
# still supplied per-invocation by state_machine.tf, since the payload varies per execution.
resource "aws_ecs_task_definition" "ecs_ingestion_task_definition" {
  for_each = local.ingestion_active ? toset(local.ingestion_steps) : []

  family                   = "${local.name_prefix}-${each.key}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.ingestion_task_cpu
  memory                   = var.ingestion_task_memory
  execution_role_arn       = aws_iam_role.ingestion_execution_role[0].arn
  task_role_arn            = contains(local.graph_load_steps, each.key) ? aws_iam_role.ingestion_graph_load_task_role[0].arn : aws_iam_role.ingestion_task_role[0].arn

  container_definitions = jsonencode([
    {
      name      = "${local.name_prefix}-container"
      image     = local.resolved_ingestion_image
      essential = true

      # No secrets block: local.workflow_environment is plain config, and the DB legs authenticate
      # with an RDS IAM token the task mints itself under the task role's rds-db:connect grant
      # (ingestion_task_permissions_document above), so there are no credentials to inject.
      environment = concat(
        [{ name = "THOR_STEP", value = each.key }],
        [for k, v in local.workflow_environment : { name = k, value = v }],
      )

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.ingestion_task_log_group[0].name
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = each.key
        }
      }
    }
  ])

  tags = var.tags

  depends_on = [
    aws_iam_role_policy_attachment.ingestion_execution_policy_attachment,
    aws_iam_role_policy.ingestion_task_permissions,
    aws_iam_role_policy.ingestion_graph_load_task_permissions,
  ]

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
