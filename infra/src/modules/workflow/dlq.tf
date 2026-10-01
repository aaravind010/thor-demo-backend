# The workflow's dead-letter queue, when it has one. Written to two ways: the state machine's
# SendToDeadLetterQueue on retry exhaustion, and SQS redrive from the trigger queue when the Pipe
# repeatedly fails to deliver.
#
# Created only when the definition names $${dlq_url} — see local.has_dlq. Not every workflow has one:
# a workflow started by another workflow rather than by a queue has nothing to redrive from, and
# records a failure in its own workflow row instead. Creating one regardless left ATRE with an empty
# queue nothing ever wrote to.
#
# The trigger queue itself lives in modules/workflow_trigger_s3, so that a workflow which is not
# started by an S3 upload simply does not instantiate it. The redrive_allow_policy names that queue
# by a built ARN string rather than a resource reference — the same trick the old module used within
# its own file, and the reason the split needs no dependency back from here to the trigger.

resource "aws_sqs_queue" "dlq" {
  count = local.active && local.has_dlq ? 1 : 0

  name                      = "${local.name_prefix}-dlq"
  message_retention_seconds = 1209600 # 14 days — max, gives time to notice/investigate before loss
  tags                      = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_sqs_queue_redrive_allow_policy" "dlq" {
  count = local.active && local.has_dlq ? 1 : 0

  queue_url = aws_sqs_queue.dlq[0].id

  redrive_allow_policy = jsonencode({
    redrivePermission = "byQueue"
    sourceQueueArns   = ["arn:aws:sqs:${var.aws_region}:${var.account_id}:${local.trigger_queue_name}"]
  })
}
