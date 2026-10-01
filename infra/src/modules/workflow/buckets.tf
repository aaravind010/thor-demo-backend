# Buckets this workflow owns. Note what is *not* here: the uploads bucket. modules/uploads owns it,
# it holds live tenant scan data, and it is passed to modules/workflow_trigger_s3 as an input. The
# old modules/ingestion declared a bucket with the same name modules/uploads already created, which
# would have failed the first enable_ingestion apply with BucketAlreadyOwnedByYou.

# --- Distributed Map results ------------------------------------------------------------------
#
# Built in rather than declared through var.buckets, because nothing but the state machine's
# ResultWriter touches it. Kept separate from any workflow-declared bucket so a step's write grant
# can never reach Map output.
#
# Created exactly when the definition names ${map_results_bucket}, which is narrower than "has a
# Map": a Distributed Map whose item outputs are read by a later state cannot declare a ResultWriter
# at all, since it replaces the inline result. This used to key off local.active alone, so every
# workflow got a bucket - ingestion's was used, and one with no Map at all still paid for an empty
# bucket with a lifecycle rule.

resource "aws_s3_bucket" "map_results" {
  count = local.active && local.has_map_results ? 1 : 0

  bucket = "${local.name_prefix}-results-${var.account_id}"
  tags   = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_s3_bucket_public_access_block" "map_results" {
  count = local.active && local.has_map_results ? 1 : 0

  bucket                  = aws_s3_bucket.map_results[0].id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_ownership_controls" "map_results" {
  count = local.active && local.has_map_results ? 1 : 0

  bucket = aws_s3_bucket.map_results[0].id

  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "map_results" {
  count = local.active && local.has_map_results ? 1 : 0

  bucket = aws_s3_bucket.map_results[0].id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "map_results" {
  count = local.active && local.has_map_results ? 1 : 0

  bucket = aws_s3_bucket.map_results[0].id

  rule {
    id     = "expire-map-results"
    status = "Enabled"

    filter {}

    expiration {
      days = var.map_results_retention_days
    }
  }
}

# --- workflow-declared buckets ------------------------------------------------------------------
#
# One per var.buckets entry. Ingestion uses this for its Neptune graph-load payloads, which used to
# be written into the uploads bucket — where the unfiltered s3:ObjectCreated:* notification fed them
# straight back into the pipeline. Splitting them out means Neptune's loader role reads exactly one
# bucket, and it is neither the one connectors write to nor the one holding Map output.

resource "aws_s3_bucket" "workflow" {
  for_each = local.active ? var.buckets : {}

  bucket = "${local.name_prefix}-${each.key}-${var.account_id}"
  tags   = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_s3_bucket_public_access_block" "workflow" {
  for_each = local.active ? var.buckets : {}

  bucket                  = aws_s3_bucket.workflow[each.key].id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_ownership_controls" "workflow" {
  for_each = local.active ? var.buckets : {}

  bucket = aws_s3_bucket.workflow[each.key].id

  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "workflow" {
  for_each = local.active ? var.buckets : {}

  bucket = aws_s3_bucket.workflow[each.key].id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "workflow" {
  for_each = local.active ? { for k, b in var.buckets : k => b if b.retention_days != null } : {}

  bucket = aws_s3_bucket.workflow[each.key].id

  rule {
    id     = "expire-${each.key}"
    status = "Enabled"

    filter {}

    expiration {
      days = each.value.retention_days
    }
  }
}
