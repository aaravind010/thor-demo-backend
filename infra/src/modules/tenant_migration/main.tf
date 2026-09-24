terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

# Tenant schema migrations (expand phase): Step Functions runs the Atlas runner image
# (migrations/tenant) on Fargate to snapshot live schemas, waits while CI plans and a reviewer
# approves, then applies per tenant. The runner
# code, Dockerfile and ASL live in migrations/tenant; this module only provisions the AWS side.
locals {
  name_prefix    = "thor-${var.environment}-tenant-migration"
  bucket_name    = "${local.name_prefix}-${var.account_id}"
  container_name = "${local.name_prefix}-runner"

  # The runner connects through the RDS Proxy (tenant_routing.cluster_endpoint), so rds-db:connect
  # is scoped by proxy resource ID. thor_app reads the tenant list; tenant_*_rw owns each tenant's
  # schema and runs the DDL.
  rds_db_arn_prefix = "arn:aws:rds-db:${var.aws_region}:${var.account_id}:dbuser:${var.rds_proxy_resource_id}"
}

# Plans, approval tokens and applied-state markers. Nothing but the three migration principals
# (GitHub OIDC deploy role, runner task role, state machine role) may touch it — enforced by an
# explicit Deny in the bucket policy, so even an admin session is refused.
resource "aws_s3_bucket" "migration_bucket" {
  bucket = local.bucket_name

  tags = merge(var.tags, {
    Name = local.bucket_name
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_s3_bucket_public_access_block" "migration_bucket_pab" {
  bucket = aws_s3_bucket.migration_bucket.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_ownership_controls" "migration_bucket_ownership" {
  bucket = aws_s3_bucket.migration_bucket.id

  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "migration_bucket_sse" {
  bucket = aws_s3_bucket.migration_bucket.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_versioning" "migration_bucket_versioning" {
  bucket = aws_s3_bucket.migration_bucket.id

  versioning_configuration {
    status = "Enabled"
  }
}

# runs/<runId>/ is per-execution scratch (plans, summaries, tokens); state/ is the applied-state
# record every later run reads, so it never expires.
resource "aws_s3_bucket_lifecycle_configuration" "migration_bucket_lifecycle" {
  bucket = aws_s3_bucket.migration_bucket.id

  rule {
    id     = "expire-runs"
    status = "Enabled"

    filter {
      prefix = "runs/"
    }

    expiration {
      days = var.run_retention_days
    }

    noncurrent_version_expiration {
      noncurrent_days = var.run_retention_days
    }
  }
}

data "aws_iam_policy_document" "migration_bucket_policy_document" {
  statement {
    sid     = "DenyInsecureTransport"
    effect  = "Deny"
    actions = ["s3:*"]

    principals {
      type        = "*"
      identifiers = ["*"]
    }

    resources = [
      aws_s3_bucket.migration_bucket.arn,
      "${aws_s3_bucket.migration_bucket.arn}/*",
    ]

    condition {
      test     = "Bool"
      variable = "aws:SecureTransport"
      values   = ["false"]
    }
  }

  # aws:PrincipalArn resolves an assumed-role session to its role ARN. The OIDC role is also the
  # identity Terraform runs as in CI, which is what keeps this policy manageable after it's applied.
  statement {
    sid     = "DenyAllButMigrationPrincipals"
    effect  = "Deny"
    actions = ["s3:*"]

    principals {
      type        = "*"
      identifiers = ["*"]
    }

    resources = [
      aws_s3_bucket.migration_bucket.arn,
      "${aws_s3_bucket.migration_bucket.arn}/*",
    ]

    condition {
      test     = "ArnNotEquals"
      variable = "aws:PrincipalArn"
      values = [
        var.github_oidc_role_arn,
        aws_iam_role.runner_task_role.arn,
        aws_iam_role.state_machine_role.arn,
      ]
    }
  }
}

resource "aws_s3_bucket_policy" "migration_bucket_policy" {
  bucket = aws_s3_bucket.migration_bucket.id
  policy = data.aws_iam_policy_document.migration_bucket_policy_document.json

  depends_on = [aws_s3_bucket_public_access_block.migration_bucket_pab]
}
