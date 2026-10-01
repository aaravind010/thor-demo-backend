output "state_machine_arn" {
  description = "The workflow's state machine. Consumed by modules/workflow_trigger_s3's Pipe target and by whatever starts an execution."
  value       = local.active ? aws_sfn_state_machine.workflow[0].arn : ""
}

output "dlq_arn" {
  description = "Dead-letter queue ARN, or \"\" when the definition names no $${dlq_url} and so has no queue. modules/workflow_trigger_s3 points its trigger queue's redrive policy here."
  value       = local.active && local.has_dlq ? aws_sqs_queue.dlq[0].arn : ""
}

output "trigger_queue_name" {
  description = "The name modules/workflow_trigger_s3 must give its queue. Both sides derive it from the same rule so neither has to read the other's resources — that is what keeps the dependency one-way."
  value       = local.trigger_queue_name
}

output "bucket_names" {
  description = "var.buckets key => bucket name."
  value       = { for key, _ in var.buckets : key => "${local.name_prefix}-${key}-${var.account_id}" }
}

output "bucket_arns" {
  description = "var.buckets key => bucket ARN. Neptune's bulk-load role reads the graph-load bucket through this."
  value       = local.bucket_arn
}

output "map_results_bucket" {
  description = "Distributed Map ResultWriter target, or \"\" when the definition declares no ResultWriter."
  value       = local.active && local.has_map_results ? aws_s3_bucket.map_results[0].bucket : ""
}
