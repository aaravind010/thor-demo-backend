# Ingress lives in separate aws_vpc_security_group_ingress_rule resources below, not inline, since an inline block indexing into sibling instances of this same for_each resource creates a real Terraform dependency cycle.
resource "aws_security_group" "service" {
  for_each = local.active_services

  name        = "${local.name_prefix[each.key]}-service-sg"
  description = each.value.expose_via_nlb ? "ECS service ingress from the NLB's security group, egress open (no NAT/IGW route today, so this only reaches the VPC + endpoints in practice)" : "ECS service ingress restricted to public-facing peer services only, egress open (no NAT/IGW route today, so this only reaches the VPC + endpoints in practice)"
  vpc_id      = var.vpc_id

  # cidr_blocks is 0.0.0.0/0, not var.vpc_cidr — no NAT/IGW route exists for private subnets today, so this only widens the boundary (not actual reachability) until a NAT Gateway is added, at which point it grants full internet egress immediately with no separate decision.
  egress {
    description = "All traffic"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(var.tags, {
    Name = "${local.name_prefix[each.key]}-service-sg"
  })
}

# Public-facing services (thor-api): ingress restricted to the NLB's own security group, not the whole VPC CIDR.
resource "aws_vpc_security_group_ingress_rule" "public_from_nlb" {
  for_each = local.public_services

  security_group_id            = aws_security_group.service[each.key].id
  description                  = "From the NLB"
  from_port                    = each.value.container_port
  to_port                      = each.value.container_port
  ip_protocol                  = "tcp"
  referenced_security_group_id = aws_security_group.nlb[each.key].id

  tags = var.tags
}

# NLB ingress: reached via VPC Link from API Gateway, so this allows the VPC Link's ENIs (anywhere in the VPC) rather than the internet.
resource "aws_vpc_security_group_ingress_rule" "nlb_from_vpc" {
  for_each = local.public_services

  security_group_id = aws_security_group.nlb[each.key].id
  description       = "From within the VPC"
  from_port         = each.value.nlb_listener_port
  to_port           = each.value.nlb_listener_port
  ip_protocol       = "tcp"
  cidr_ipv4         = var.vpc_cidr

  tags = var.tags
}

# Blue/green test listener — same scoping as the production listener above.
resource "aws_vpc_security_group_ingress_rule" "nlb_test_from_vpc" {
  for_each = local.public_services

  security_group_id = aws_security_group.nlb[each.key].id
  description       = "Blue/green test listener, from within the VPC"
  from_port         = each.value.nlb_test_listener_port
  to_port           = each.value.nlb_test_listener_port
  ip_protocol       = "tcp"
  cidr_ipv4         = var.vpc_cidr

  tags = var.tags
}

# Internal-only services: ingress restricted to each public-facing peer's own security group, built as one map merged across every non-public service × public peer so it still holds if more than one service is ever expose_via_nlb = true.
resource "aws_vpc_security_group_ingress_rule" "internal_from_public_peers" {
  for_each = merge([
    for svc_key, svc in local.active_services : svc.expose_via_nlb ? {} : {
      for peer_key in keys(local.public_services) : "${svc_key}-from-${peer_key}" => {
        service = svc_key
        peer    = peer_key
      }
    }
  ]...)

  security_group_id            = aws_security_group.service[each.value.service].id
  description                  = "From public-facing peer service: ${each.value.peer}"
  referenced_security_group_id = aws_security_group.service[each.value.peer].id
  from_port                    = local.active_services[each.value.service].container_port
  to_port                      = local.active_services[each.value.service].container_port
  ip_protocol                  = "tcp"

  tags = var.tags
}
