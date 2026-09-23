output "cluster_identifier" {
  value = aws_rds_cluster.aurora_cluster.cluster_identifier
}

output "cluster_resource_id" {
  description = "Immutable cluster resource ID — the middle segment of rds-db:connect ARNs (arn:aws:rds-db:<region>:<acct>:dbuser:<cluster_resource_id>/<dbuser>)"
  value       = aws_rds_cluster.aurora_cluster.cluster_resource_id
}

output "master_user_secret_arn" {
  description = "ARN of the Secrets-Manager-managed master credential — read by the in-VPC db_bootstrap Lambda to mint the platform roles, and directly usable as a value in a service's own secrets map (infra/src/variables.tf's services schema)"
  value       = aws_rds_cluster.aurora_cluster.master_user_secret[0].secret_arn
}

output "endpoint" {
  description = "Writer endpoint"
  value       = aws_rds_cluster.aurora_cluster.endpoint
}

output "reader_endpoint" {
  value = aws_rds_cluster.aurora_cluster.reader_endpoint
}

output "port" {
  value = aws_rds_cluster.aurora_cluster.port
}

output "database_name" {
  value = aws_rds_cluster.aurora_cluster.database_name
}

output "security_group_id" {
  value = aws_security_group.aurora_security_group.id
}
