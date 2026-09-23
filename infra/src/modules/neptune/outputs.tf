output "cluster_identifier" {
  value = aws_neptune_cluster.neptune.cluster_identifier
}

output "cluster_arn" {
  value = aws_neptune_cluster.neptune.arn
}

output "cluster_resource_id" {
  description = "Needed for the neptune-db IAM policy ARN (arn:aws:neptune-db:<region>:<account>:<cluster-resource-id>/...) once the Gremlin client is wired up — not usable until then, cluster_identifier/ARN alone aren't enough for that action namespace."
  value       = aws_neptune_cluster.neptune.cluster_resource_id
}

output "endpoint" {
  description = "Writer endpoint"
  value       = aws_neptune_cluster.neptune.endpoint
}

output "reader_endpoint" {
  value = aws_neptune_cluster.neptune.reader_endpoint
}

output "port" {
  value = aws_neptune_cluster.neptune.port
}

output "security_group_id" {
  value = aws_security_group.neptune.id
}

output "bulk_load_role_arn" {
  description = "The cluster-attached role a loader job names as iamRoleArn — modules/ingestion hands it to graph-load-start as THOR_GRAPH_BULKLOAD_IAM_ROLE_ARN. \"\" when create_bulk_load_role is off."
  value       = var.create_bulk_load_role ? aws_iam_role.bulk_load[0].arn : ""
}
