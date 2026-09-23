# One-off adoption of the ingestion bucket. It was created by this config at this address (its
# default_tags are present and neither local.bucket_name nor the count index has changed since
# 50f2d0c), but the state entry never persisted, so every apply since re-attempts CreateBucket and
# gets 409 BucketAlreadyOwnedByYou — a failed create is never recorded, so retrying cannot clear it.
#
# Gated to dev: the id is dev-specific and qa shares the same AWS account, so an ungated block would
# have the qa plan adopt dev's bucket. Also gated on enable_ingestion, which zeroes the bucket's count
# — an import whose target has no configuration is a plan error, not a no-op. Delete this file once
# the dev apply has run with ingestion on.
import {
  for_each = var.environment == "dev" && var.enable_ingestion ? toset([0]) : toset([])

  to = module.ingestion.aws_s3_bucket.s3_ingestion[each.value]
  id = "thor-dev-ingestion-324938817870"
}
