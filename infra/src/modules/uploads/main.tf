terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

data "aws_caller_identity" "current" {}

locals {
  # Globally unique — bucket names share a namespace across all of AWS, not just this account.
  # thor-<env>-* is the convention every deploy-role IAM grant is scoped to; a name that leads with
  # the purpose instead (thor-uploads-<env>-*) matches no grant and fails CreateBucket at apply.
  bucket_name = "thor-${var.environment}-ingestion-${data.aws_caller_identity.current.account_id}"
}

# Tenant scan-data uploads. Connectors never reach this bucket directly: Thor.TaskApi presigns a
# PUT with its own task-role credentials after resolving the tenant, so the bucket stays private
# and the presigned URL is the only write path.
#
# This is also the ingestion pipeline's entry point — an object landing here is what triggers
# S3 -> SQS -> Step Functions. This module owns the bucket itself; modules/ingestion consumes it
# (notification + pipeline) and must not create one of its own.
resource "aws_s3_bucket" "thor-uploads-bucket" {
  bucket = local.bucket_name

  tags = merge(var.tags, {
    Name = local.bucket_name
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_s3_bucket_public_access_block" "thor-uploads-bucket-pab" {
  bucket = aws_s3_bucket.thor-uploads-bucket.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_ownership_controls" "thor-uploads-bucket-ownership" {
  bucket = aws_s3_bucket.thor-uploads-bucket.id

  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "thor-uploads-bucket-sse" {
  bucket = aws_s3_bucket.thor-uploads-bucket.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_versioning" "thor-uploads-bucket-versioning" {
  bucket = aws_s3_bucket.thor-uploads-bucket.id

  versioning_configuration {
    status = "Enabled"
  }
}

# Presigned PUTs are plain HTTPS from the connector's own network, so the only transport
# guarantee the bucket itself can make is to refuse anything that arrived over HTTP.
data "aws_iam_policy_document" "deny_insecure_transport" {
  statement {
    sid     = "DenyInsecureTransport"
    effect  = "Deny"
    actions = ["s3:*"]

    principals {
      type        = "*"
      identifiers = ["*"]
    }

    resources = [
      aws_s3_bucket.thor-uploads-bucket.arn,
      "${aws_s3_bucket.thor-uploads-bucket.arn}/*",
    ]

    condition {
      test     = "Bool"
      variable = "aws:SecureTransport"
      values   = ["false"]
    }
  }
}

resource "aws_s3_bucket_policy" "thor-uploads-bucket-policy" {
  bucket = aws_s3_bucket.thor-uploads-bucket.id
  policy = data.aws_iam_policy_document.deny_insecure_transport.json

  depends_on = [aws_s3_bucket_public_access_block.thor-uploads-bucket-pab]
}
