# Consumed two ways: the platform's rds_proxy/neptune ingress maps read this directly, and each
# workflow unit reads its own entry to place its ECS tasks and Lambda ENIs.
output "security_group_ids" {
  description = "Workflow name => security group ID."
  value       = { for name, sg in aws_security_group.workflow : name => sg.id }
}
