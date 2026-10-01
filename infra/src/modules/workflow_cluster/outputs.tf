output "cluster_name" {
  description = "Shared workflow cluster name, used in each workflow's ecs:runTask.sync Parameters."
  value       = aws_ecs_cluster.workflows.name
}

output "cluster_arn" {
  description = "Shared workflow cluster ARN, for the state machine role's ecs:RunTask/StopTask grant."
  value       = aws_ecs_cluster.workflows.arn
}
