output "queue_arn" {
  description = "Trigger queue ARN."
  value       = local.active ? aws_sqs_queue.trigger[0].arn : ""
}

output "handler_function_arn" {
  description = "The trigger handler Lambda."
  value       = local.active ? aws_lambda_function.handler[0].arn : ""
}
