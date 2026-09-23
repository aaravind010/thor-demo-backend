output "ecr_repository_url" {
  description = "Populated regardless of enable_ingestion — a repo must exist before the flag can turn on"
  value       = aws_ecr_repository.ecr_ingestion.repository_url
}

output "ecr_repository_arn" {
  value = aws_ecr_repository.ecr_ingestion.arn
}

output "queue_arn" {
  value = var.enable_ingestion ? aws_sqs_queue.sqs_ingestion[0].arn : null
}

output "queue_url" {
  value = var.enable_ingestion ? aws_sqs_queue.sqs_ingestion[0].id : null
}

output "dlq_arn" {
  value = var.enable_ingestion ? aws_sqs_queue.sqs_ingestion_dlq[0].arn : null
}

output "state_machine_arn" {
  value = var.enable_ingestion ? aws_sfn_state_machine.sfn_ingestion[0].arn : null
}

output "create_manifest_function_arn" {
  value = var.enable_ingestion ? aws_lambda_function.manifest_lambda_function[0].arn : null
}

output "ingestion_driver_function_arn" {
  value = var.enable_ingestion ? aws_lambda_function.ingestion_driver[0].arn : null
}

output "ingestion_step_function_arns" {
  description = "Map of THOR_STEP -> its Lambda function ARN (the Lambda compute target)"
  value       = var.enable_ingestion ? { for k, v in aws_lambda_function.ingestion_step : k => v.arn } : null
}

output "ingestion_task_definition_arns" {
  description = "Map of THOR_STEP -> its ECS task definition ARN (the ECS compute target)"
  value       = var.enable_ingestion ? { for k, v in aws_ecs_task_definition.ecs_ingestion_task_definition : k => v.arn } : null
}

output "map_results_bucket_name" {
  description = "Where extract-stage's Distributed Map writes per-file results, under ingestion-map-results/<ScanManifestId>/<ExecutionName>/"
  value       = var.enable_ingestion ? aws_s3_bucket.s3_map_results[0].bucket : null
}

output "bucket_arn" {
  description = "The ingestion (upload + graph bulk-load CSV) bucket — module.neptune's loader role is scoped to it at the root"
  value       = var.enable_ingestion ? aws_s3_bucket.s3_ingestion[0].arn : null
}

output "task_security_group_id" {
  description = "Passed into module.rds_proxy's and module.neptune's own consumer/allowed security-group maps at the root, so the ingestion tasks and Lambda functions (which share this SG) can actually reach both"
  value       = var.enable_ingestion ? aws_security_group.ingestion_task_sg[0].id : null
}
