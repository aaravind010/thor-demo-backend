output "vpc_id" {
  value = aws_vpc.thor-vpc.id
}

output "vpc_cidr" {
  value = aws_vpc.thor-vpc.cidr_block
}

output "public_subnet_ids" {
  value = aws_subnet.public[*].id
}

output "private_subnet_ids" {
  value = aws_subnet.private[*].id
}

output "public_route_table_id" {
  value = aws_route_table.public.id
}

output "private_route_table_id" {
  value = aws_route_table.private.id
}

output "vpc_endpoints_security_group_id" {
  value = var.enable_vpc_endpoints ? aws_security_group.vpc_endpoints[0].id : null
}
