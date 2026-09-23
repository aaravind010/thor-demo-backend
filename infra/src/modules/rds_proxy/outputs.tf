output "endpoint" {
  description = "What thor/task-api should connect to instead of Aurora's own endpoint, to actually get the pooling benefit"
  value       = aws_db_proxy.rds_proxy.endpoint
}

# The "prx-..." id, which is what an rds-db:connect ARN must name for the client->proxy leg —
# the cluster resource ID authorizes only a direct-to-Aurora connection. aws_db_proxy exposes no
# resource-id attribute, so it comes out of the ARN's last field
# (arn:aws:rds:<region>:<acct>:db-proxy:prx-...).
output "proxy_resource_id" {
  description = "RDS Proxy resource ID (prx-...) — the middle segment of rds-db:connect ARNs for anything connecting through the proxy"
  value       = element(split(":", aws_db_proxy.rds_proxy.arn), 6)
}

output "rds_proxy_security_group_id" {
  description = "Passed into the aurora module's own allowed_security_group_ids, so the proxy itself can reach Aurora"
  value       = aws_security_group.rds_proxy_security_group.id
}
