terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

data "aws_availability_zones" "available" {
  state = "available"
}

data "aws_region" "current" {}

locals {
  azs         = slice(data.aws_availability_zones.available.names, 0, var.az_count)
  name_prefix = "thor-${var.environment}"

  # Only what's actually used today — uncomment the rest as each feature gets wired in.
  interface_endpoints = var.enable_vpc_endpoints ? toset([
    "ecr.api",
    "ecr.dkr",
    "logs",
    "secretsmanager",
    "rds-data",
    "cognito-idp",
    "states", # modules/ingestion's in-VPC CreateManifest Lambda calls states:StartExecution
    # "ssm",         # ECS Exec / Parameter Store — not used yet
    # "ssmmessages", # ECS Exec — not used yet
    # "ec2messages", # ECS Exec — not used yet
    # "sts",         # direct STS calls from the app — not used yet
    # "xray",        # X-Ray tracing — IAM permission exists, no daemon/tracing wired in yet
    # "monitoring",  # custom CloudWatch metrics — not used yet
  ]) : toset([])
}

resource "aws_vpc" "thor-vpc" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-vpc"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Private subnets — ECS Fargate tasks, Aurora / RDS Proxy, Graph DB
resource "aws_subnet" "private" {
  count             = length(var.private_subnet_cidrs)
  vpc_id            = aws_vpc.thor-vpc.id
  cidr_block        = var.private_subnet_cidrs[count.index]
  availability_zone = local.azs[count.index]

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-private-${local.azs[count.index]}"
    Tier = "private"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# AWS services are reached through the VPC endpoints below; nat.tf adds a 0.0.0.0/0 NAT route here when nat_gateway_mode = "single".
resource "aws_route_table" "private" {
  vpc_id = aws_vpc.thor-vpc.id

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-private-rt"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_route_table_association" "private" {
  count          = length(aws_subnet.private)
  subnet_id      = aws_subnet.private[count.index].id
  route_table_id = local.per_az_nat ? aws_route_table.private_az[count.index].id : aws_route_table.private.id

  # per_az: move a subnet only once its new route table already carries the S3 endpoint route.
  depends_on = [aws_vpc_endpoint_route_table_association.s3_private_az]
}

# Adopts the VPC's default NACL (every subnet uses it). Inbound: anything from inside the VPC — the private subnets
# reach the NAT on 443, so the public subnets must accept that — plus return traffic from the internet on ephemeral
# ports. Outbound: everything; egress is controlled by security groups and the DNS Firewall, not here.
resource "aws_default_network_acl" "this" {
  default_network_acl_id = aws_vpc.thor-vpc.default_network_acl_id

  ingress {
    rule_no    = 90
    action     = "allow"
    protocol   = "-1"
    cidr_block = var.vpc_cidr
    from_port  = 0
    to_port    = 0
  }

  ingress {
    rule_no    = 100
    action     = "allow"
    protocol   = "tcp"
    cidr_block = "0.0.0.0/0"
    from_port  = 1024
    to_port    = 65535
  }

  egress {
    rule_no    = 100
    action     = "allow"
    protocol   = "-1"
    cidr_block = "0.0.0.0/0"
    from_port  = 0
    to_port    = 0
  }

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-default-nacl"
  })

  # Subnets stay on the default NACL implicitly and can't be detached from it, so the association list isn't managed here.
  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [subnet_ids, tags, tags_all]
  }
}

# HTTPS-only access from within the VPC to the interface endpoints
resource "aws_security_group" "vpc_endpoints" {
  count       = var.enable_vpc_endpoints ? 1 : 0
  name        = "${local.name_prefix}-vpce-sg"
  description = "Allow HTTPS from within the VPC to interface endpoints"
  vpc_id      = aws_vpc.thor-vpc.id

  ingress {
    description = "HTTPS from VPC CIDR"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = [var.vpc_cidr]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-vpce-sg"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# S3 gateway endpoint — no hourly/data cost, used for ECR image layers, Terraform state, etc.
resource "aws_vpc_endpoint" "s3" {
  count             = var.enable_vpc_endpoints ? 1 : 0
  vpc_id            = aws_vpc.thor-vpc.id
  service_name      = "com.amazonaws.${data.aws_region.current.region}.s3"
  vpc_endpoint_type = "Gateway"
  route_table_ids   = [aws_route_table.private.id]

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-s3-endpoint"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_vpc_endpoint" "interface" {
  for_each            = local.interface_endpoints
  vpc_id              = aws_vpc.thor-vpc.id
  service_name        = "com.amazonaws.${data.aws_region.current.region}.${each.value}"
  vpc_endpoint_type   = "Interface"
  subnet_ids          = aws_subnet.private[*].id
  security_group_ids  = [aws_security_group.vpc_endpoints[0].id]
  private_dns_enabled = true

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-${each.value}-endpoint"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
