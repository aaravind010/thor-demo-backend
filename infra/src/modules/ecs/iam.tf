data "aws_iam_policy_document" "ecs_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

# One execution role per service (never shared), scoped to only the secret ARNs that service actually uses.
resource "aws_iam_role" "execution" {
  for_each = local.active_services

  name                 = "${local.name_prefix[each.key]}-execution"
  assume_role_policy   = data.aws_iam_policy_document.ecs_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags
}

resource "aws_iam_role_policy_attachment" "execution_managed" {
  for_each = local.active_services

  role       = aws_iam_role.execution[each.key].name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

data "aws_iam_policy_document" "secrets_access" {
  for_each = { for k, v in local.active_services : k => v if length(v.secrets) > 0 }

  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = values(each.value.secrets)
  }
}

resource "aws_iam_role_policy" "execution_secrets" {
  for_each = data.aws_iam_policy_document.secrets_access

  name   = "secrets-access"
  role   = aws_iam_role.execution[each.key].id
  policy = each.value.json
}

# Task role — the application's own runtime permissions; X-Ray is the only baseline grant, add more per service as needed (e.g. Bedrock, DB access).
resource "aws_iam_role" "task" {
  for_each = local.active_services

  name                 = "${local.name_prefix[each.key]}-task"
  assume_role_policy   = data.aws_iam_policy_document.ecs_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags
}

resource "aws_iam_role_policy_attachment" "task_xray" {
  for_each = local.active_services

  role       = aws_iam_role.task[each.key].name
  policy_arn = "arn:aws:iam::aws:policy/AWSXRayDaemonWriteAccess"
}
