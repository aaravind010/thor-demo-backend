# The two compute targets, both from the same image. Program.cs picks LambdaHost when
# AWS_LAMBDA_RUNTIME_API is set and EcsHost otherwise, so one build serves both and a step cannot
# behave differently depending on where it runs.
#
# Only the halves a step actually declares are built. The old module created both for every step,
# which meant select-compute had an ECS task definition that could never be invoked.

locals {
  # Buckets this module names are injected here so the caller does not have to predict a name this
  # module owns — the reason var.buckets carries an env_var at all.
  bucket_env = { for key, b in var.buckets : b.env_var => "${local.name_prefix}-${key}-${var.account_id}" if b.env_var != null }

  step_environment = merge(var.environment_variables, local.bucket_env)
}

resource "aws_cloudwatch_log_group" "task" {
  count = local.active && length(local.ecs_step_names) > 0 ? 1 : 0

  name              = "/ecs/${var.environment}/${local.name_prefix}"
  retention_in_days = var.log_retention_days
  tags              = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# One task definition per THOR_STEP. Baking THOR_STEP into the definition rather than only into the
# per-invocation override means any step can be run standalone with ecs:RunTask, without Step
# Functions. THOR_INPUT is supplied per invocation by the state machine, since it varies per run.
resource "aws_ecs_task_definition" "step" {
  for_each = local.active ? toset(local.ecs_step_names) : toset([])

  family                   = "${local.name_prefix}-${each.key}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.task_cpu
  memory                   = var.task_memory
  execution_role_arn       = aws_iam_role.execution[0].arn
  task_role_arn            = aws_iam_role.task[local.step_role[each.key]].arn

  container_definitions = jsonencode([
    {
      name      = "${local.name_prefix}-container"
      image     = local.image
      essential = true

      # No secrets block: step_environment is plain config, and the DB legs authenticate with an RDS
      # IAM token the task mints itself, so there are no credentials to inject.
      environment = concat(
        [{ name = "THOR_STEP", value = each.key }],
        [for k, v in local.step_environment : { name = k, value = v }],
      )

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.task[0].name
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = each.key
        }
      }
    }
  ])

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_cloudwatch_log_group" "step" {
  for_each = local.active ? toset(local.lambda_step_names) : toset([])

  name              = "/aws/lambda/${local.name_prefix}-${each.key}"
  retention_in_days = var.log_retention_days
  tags              = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# package_type = "Image" against the same repository and tag the task definitions use — no separate
# zip, no second build, and no update-function-code step in CI. Terraform owns the tag, so the
# function, the task definition and the state machine's pinned revision move together or not at all.
resource "aws_lambda_function" "step" {
  for_each = local.active ? toset(local.lambda_step_names) : toset([])

  function_name = "${local.name_prefix}-${each.key}"
  role          = aws_iam_role.lambda[local.step_role[each.key]].arn
  package_type  = "Image"
  image_uri     = local.image
  timeout       = var.lambda_timeout
  memory_size   = var.lambda_memory_size

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [var.security_group_id]
  }

  # THOR_INPUT isn't set here: on Lambda the step's input is the invocation event, not an env var.
  # Everything else is identical to the ECS branch — step_environment carries the RDS IAM user and
  # region both targets mint a token with.
  environment {
    variables = merge(local.step_environment, { THOR_STEP = each.key })
  }

  tags = var.tags

  depends_on = [
    aws_cloudwatch_log_group.step,
    aws_iam_role_policy_attachment.lambda_vpc_access,
    aws_iam_role_policy.lambda,
  ]

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
