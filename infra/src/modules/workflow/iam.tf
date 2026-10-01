# Compute roles, generated from the variant set rather than hand-written. The old module had nine
# hand-maintained roles for one workflow; here each variant produces exactly two (an ECS task role
# and a Lambda role sharing one policy), so the two compute targets cannot drift in what the same
# step code is allowed to do.
#
# Variants exist because IAM cannot distinguish task definitions on a shared role — no condition key
# carries the task family — so a grant that only some steps should have needs its own role.

locals {
  # Bucket ARNs are built from the naming rule, not read off the resources, so this evaluates
  # cleanly when the workflow is disabled and the buckets do not exist.
  bucket_arn = { for key, _ in var.buckets : key => "arn:aws:s3:::${local.name_prefix}-${key}-${var.account_id}" }

  variant_statements = { for variant in local.role_variant_names : variant => concat(
    var.base_policy_statements,
    lookup(var.role_variants, variant, []),
    [for key, b in var.buckets : {
      actions   = ["s3:PutObject"]
      resources = ["${local.bucket_arn[key]}/*"]
    } if contains(b.write_roles, variant)],
    [for key, b in var.buckets : {
      actions   = ["s3:GetObject", "s3:ListBucket"]
      resources = [local.bucket_arn[key], "${local.bucket_arn[key]}/*"]
    } if contains(b.read_roles, variant)],
  ) }

  # An inline policy with no statements is rejected, so a variant that adds nothing gets a role and
  # no policy. That is a real case: a workflow with no buckets and no base statements.
  variants_with_policy = [for variant in local.role_variant_names : variant if length(local.variant_statements[variant]) > 0]
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

data "aws_iam_policy_document" "lambda_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

data "aws_iam_policy_document" "compute" {
  for_each = local.active ? toset(local.variants_with_policy) : toset([])

  dynamic "statement" {
    for_each = local.variant_statements[each.key]

    content {
      actions   = statement.value.actions
      resources = statement.value.resources
    }
  }
}

# --- ECS execution role (one, shared) -----------------------------------------------------------
#
# Pulls the image and writes container logs. Not the container's own permissions — those are the
# task role below.

resource "aws_iam_role" "execution" {
  count = local.active ? 1 : 0

  name                 = "${local.name_prefix}-execution"
  assume_role_policy   = data.aws_iam_policy_document.ecs_task_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_iam_role_policy_attachment" "execution" {
  count = local.active ? 1 : 0

  role       = aws_iam_role.execution[0].name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

# --- per-variant compute roles ------------------------------------------------------------------

resource "aws_iam_role" "task" {
  for_each = local.active ? toset(local.role_variant_names) : toset([])

  name                 = "${local.name_prefix}-task-${each.key}"
  assume_role_policy   = data.aws_iam_policy_document.ecs_task_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_iam_role" "lambda" {
  for_each = local.active ? toset(local.role_variant_names) : toset([])

  name                 = "${local.name_prefix}-lambda-${each.key}"
  assume_role_policy   = data.aws_iam_policy_document.lambda_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# VPC variant of AWSLambdaBasicExecutionRole — adds the ENI management an in-VPC function needs.
resource "aws_iam_role_policy_attachment" "lambda_vpc_access" {
  for_each = local.active ? toset(local.role_variant_names) : toset([])

  role       = aws_iam_role.lambda[each.key].name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

resource "aws_iam_role_policy" "task" {
  for_each = local.active ? toset(local.variants_with_policy) : toset([])

  name   = "${local.name_prefix}-task-${each.key}-permissions"
  role   = aws_iam_role.task[each.key].id
  policy = data.aws_iam_policy_document.compute[each.key].json
}

resource "aws_iam_role_policy" "lambda" {
  for_each = local.active ? toset(local.variants_with_policy) : toset([])

  name   = "${local.name_prefix}-lambda-${each.key}-permissions"
  role   = aws_iam_role.lambda[each.key].id
  policy = data.aws_iam_policy_document.compute[each.key].json
}
