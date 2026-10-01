output "repository_urls" {
  description = "Workflow name => ECR repository URL. CI pushes <url>:<sha>; the workflow unit is then applied with that tag."
  value       = { for name, repo in aws_ecr_repository.workflow : name => repo.repository_url }
}

output "repository_arns" {
  description = "Workflow name => ECR repository ARN, for the ECS execution role's pull grant."
  value       = { for name, repo in aws_ecr_repository.workflow : name => repo.arn }
}
