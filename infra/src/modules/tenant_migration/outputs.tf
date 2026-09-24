output "bucket_name" {
  value       = aws_s3_bucket.migration_bucket.bucket
  description = "Plan/approval/state bucket"
}

output "state_machine_arn" {
  value       = aws_sfn_state_machine.migration_state_machine.arn
  description = "Tenant migration state machine — started by tenant-migrations.yml"
}

output "ecr_repository_url" {
  value       = aws_ecr_repository.runner_ecr.repository_url
  description = "Runner image repository"
}

output "task_definition_family" {
  value       = aws_ecs_task_definition.runner_task_definition.family
  description = "Task definition family CI registers new runner revisions under"
}

output "task_security_group_id" {
  value       = aws_security_group.runner_task_sg.id
  description = "Runner task SG — must be in the RDS Proxy's allowed_security_group_ids"
}
