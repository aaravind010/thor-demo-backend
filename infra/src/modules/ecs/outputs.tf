output "cluster_id" {
  value = aws_ecs_cluster.thor-ecs-cluster.id
}

output "cluster_name" {
  value = aws_ecs_cluster.thor-ecs-cluster.name
}

output "service_connect_namespace_name" {
  value = aws_service_discovery_http_namespace.thor-sc-namespace.name
}

output "service_names" {
  value = { for k, v in aws_ecs_service.thor-svc : k => v.name }
}

output "service_arns" {
  description = "Map of service name -> ECS service ARN, for scoping the GitHub Actions deploy role's ecs:UpdateService permission"
  value       = { for k, v in aws_ecs_service.thor-svc : k => v.id }
}

output "target_group_arns" {
  description = "Map of service name -> blue/primary target group ARN, only populated for publicly-exposed services"
  value       = { for k, v in aws_lb_target_group.thor-nlb-tg-blue : k => v.arn }
}

output "nlb_arns" {
  description = "Map of service name -> NLB ARN, only populated for publicly-exposed services — for a future API Gateway VPC Link to target"
  value       = { for k, v in aws_lb.thor-nlb : k => v.arn }
}

output "nlb_dns_names" {
  value = { for k, v in aws_lb.thor-nlb : k => v.dns_name }
}

output "nlb_listener_arns" {
  description = "Map of service name -> NLB listener ARN, only populated for publicly-exposed services — an HTTP API v2 VPC Link private integration targets the listener ARN directly, not a DNS-based URI"
  value       = { for k, v in aws_lb_listener.thor-nlb-listener : k => v.arn }
}

output "service_security_group_ids" {
  value = { for k, v in aws_security_group.service : k => v.id }
}

output "task_execution_role_arns" {
  value = { for k, v in aws_iam_role.execution : k => v.arn }
}

output "task_role_arns" {
  value = { for k, v in aws_iam_role.task : k => v.arn }
}

output "ecr_repository_urls" {
  description = "Map of service name -> ECR repository URL, e.g. for CI to know where to push images. Populated regardless of enable_compute."
  value       = { for k, v in aws_ecr_repository.thor-ecr-repo : k => v.repository_url }
}

output "ecr_repository_arns" {
  value = { for k, v in aws_ecr_repository.thor-ecr-repo : k => v.arn }
}
