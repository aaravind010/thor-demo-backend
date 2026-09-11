output "endpoint" {
  description = "What thor/task-api should connect to instead of Aurora's own endpoint, to actually get the pooling benefit"
  value       = aws_db_proxy.rds_proxy.endpoint
}

output "rds_proxy_security_group_id" {
  description = "Passed into the aurora module's own allowed_security_group_ids, so the proxy itself can reach Aurora"
  value       = aws_security_group.rds_proxy_security_group.id
}
