output "vpc_id" {
  value = aws_vpc.thor-vpc.id
}

output "vpc_cidr" {
  value = aws_vpc.thor-vpc.cidr_block
}

output "private_subnet_ids" {
  value = aws_subnet.private[*].id
}

output "private_route_table_id" {
  value = aws_route_table.private.id
}

output "vpc_endpoints_security_group_id" {
  value = var.enable_vpc_endpoints ? aws_security_group.vpc_endpoints[0].id : null
}

output "s3_prefix_list_id" {
  description = "The S3 gateway endpoint's prefix list id — consumers with a VPC-CIDR-scoped security group (e.g. Neptune) need an egress rule against this to reach S3 through the endpoint's route, since S3's IP range isn't part of the VPC CIDR. \"\" when VPC endpoints are disabled."
  value       = var.enable_vpc_endpoints ? aws_vpc_endpoint.s3[0].prefix_list_id : ""
}
