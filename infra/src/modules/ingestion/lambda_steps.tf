# The Lambda compute target for Thor.Workflows.Ingestion: one function per THOR_STEP, all from the
# same container image the ECS task definitions run (ecs_task.tf) — Program.cs boots LambdaEntry
# instead of WorkflowHost when it sees AWS_LAMBDA_RUNTIME_API. Which target a given execution uses
# is the driver's call (state_machine.tf's ChooseComputeTarget); ListFiles always runs here.
#
# Two roles mirror the two task roles for the same reason (ecs_task.tf's
# ingestion_graph_load_task_role): only local.graph_load_steps get s3:PutObject. The policy
# documents are the task roles' own, extended with the Aurora secret read that ECS's secrets block
# otherwise handles — Lambda has no equivalent injection, so the function must fetch it itself.

resource "aws_iam_role" "ingestion_lambda_role" {
  count = local.ingestion_active ? 1 : 0

  name                 = "${local.name_prefix}-lambda"
  assume_role_policy   = data.aws_iam_policy_document.lambda_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_iam_role" "ingestion_graph_load_lambda_role" {
  count = local.ingestion_active ? 1 : 0

  name                 = "${local.name_prefix}-graph-load-lambda"
  assume_role_policy   = data.aws_iam_policy_document.lambda_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_iam_role_policy_attachment" "ingestion_lambda_vpc_access" {
  for_each = local.ingestion_active ? {
    default    = aws_iam_role.ingestion_lambda_role[0].name
    graph-load = aws_iam_role.ingestion_graph_load_lambda_role[0].name
  } : {}

  role       = each.value
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

data "aws_iam_policy_document" "ingestion_lambda_permissions_document" {
  count = local.ingestion_active ? 1 : 0

  source_policy_documents = [data.aws_iam_policy_document.ingestion_task_permissions_document[0].json]

  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [var.aurora_secret_arn]
  }
}

data "aws_iam_policy_document" "ingestion_graph_load_lambda_permissions_document" {
  count = local.ingestion_active ? 1 : 0

  source_policy_documents = [data.aws_iam_policy_document.ingestion_graph_load_task_permissions_document[0].json]

  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [var.aurora_secret_arn]
  }
}

resource "aws_iam_role_policy" "ingestion_lambda_permissions" {
  count = local.ingestion_active ? 1 : 0

  name   = "${local.name_prefix}-lambda-permissions"
  role   = aws_iam_role.ingestion_lambda_role[0].id
  policy = data.aws_iam_policy_document.ingestion_lambda_permissions_document[0].json
}

resource "aws_iam_role_policy" "ingestion_graph_load_lambda_permissions" {
  count = local.ingestion_active ? 1 : 0

  name   = "${local.name_prefix}-graph-load-lambda-permissions"
  role   = aws_iam_role.ingestion_graph_load_lambda_role[0].id
  policy = data.aws_iam_policy_document.ingestion_graph_load_lambda_permissions_document[0].json
}

resource "aws_cloudwatch_log_group" "ingestion_step_log_group" {
  for_each = local.ingestion_active ? toset(local.ingestion_steps) : []

  name              = "/aws/lambda/${local.name_prefix}-${each.key}"
  retention_in_days = var.log_retention_days
  tags              = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_lambda_function" "ingestion_step" {
  for_each = local.ingestion_active ? toset(local.ingestion_steps) : []

  function_name = "${local.name_prefix}-${each.key}"
  role          = contains(local.graph_load_steps, each.key) ? aws_iam_role.ingestion_graph_load_lambda_role[0].arn : aws_iam_role.ingestion_lambda_role[0].arn
  package_type  = "Image"
  image_uri     = local.resolved_ingestion_image
  timeout       = var.ingestion_lambda_timeout
  memory_size   = var.ingestion_lambda_memory_size

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [aws_security_group.ingestion_task_sg[0].id]
  }

  # THOR_INPUT isn't set here: on Lambda the step's input is the invocation event, not an env var.
  # THOR_MASTERDB_SECRET_ARN is readable (roles above) but Composition/TenantConnectionManagerFactory
  # doesn't resolve it yet — it still expects THOR_MASTERDB_USER/PASSWORD, which only ECS's secrets
  # block can inject. Until that factory resolves the secret ARN, these functions can't open a
  # Master DB connection.
  environment {
    variables = merge(local.workflow_environment, {
      THOR_STEP                = each.key
      THOR_MASTERDB_SECRET_ARN = var.aurora_secret_arn
    })
  }

  tags = var.tags

  depends_on = [
    aws_cloudwatch_log_group.ingestion_step_log_group,
    aws_iam_role_policy_attachment.ingestion_lambda_vpc_access,
    aws_iam_role_policy.ingestion_lambda_permissions,
    aws_iam_role_policy.ingestion_graph_load_lambda_permissions,
  ]

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
