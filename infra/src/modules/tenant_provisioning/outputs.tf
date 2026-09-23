output "state_machine_arn" {
  description = "ARN of the tenant-provisioning state machine — the GitHub Actions trigger calls StartExecution on this"
  value       = aws_sfn_state_machine.provisioning.arn
}

output "state_machine_name" {
  value = aws_sfn_state_machine.provisioning.name
}

output "provisioning_security_group_id" {
  description = "Security group of the in-VPC DB Lambdas (already granted ingress to Aurora by this module)"
  value       = aws_security_group.provisioning.id
}

output "function_arns" {
  description = "Map of step key -> Lambda ARN"
  value       = { for k, f in aws_lambda_function.fn : k => f.arn }
}
