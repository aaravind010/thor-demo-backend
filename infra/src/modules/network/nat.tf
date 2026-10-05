# NAT Gateway egress for the private subnets — lets ECS services ship logs to an external vendor, and
# lets Thor.Api call the WebSocket API's @connections endpoint, which no execute-api VPC endpoint can carry.
# "single": one NAT, routed from the existing shared private route table (existing associations untouched).
# "per_az": one NAT per AZ, each private subnet moved onto its own AZ's route table.
# "none":   nothing here is created.

locals {
  nat_enabled = var.nat_gateway_mode != "none"
  per_az_nat  = var.nat_gateway_mode == "per_az"
  nat_count   = local.per_az_nat ? length(var.private_subnet_cidrs) : (local.nat_enabled ? 1 : 0)
}

resource "aws_internet_gateway" "this" {
  count  = local.nat_enabled ? 1 : 0
  vpc_id = aws_vpc.thor-vpc.id

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-igw"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Public subnets — hold only the NAT Gateways.
resource "aws_subnet" "public" {
  count                   = local.nat_enabled ? length(var.public_subnet_cidrs) : 0
  vpc_id                  = aws_vpc.thor-vpc.id
  cidr_block              = var.public_subnet_cidrs[count.index]
  availability_zone       = local.azs[count.index]
  map_public_ip_on_launch = false

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-public-${local.azs[count.index]}"
    Tier = "public"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_route_table" "public" {
  count  = local.nat_enabled ? 1 : 0
  vpc_id = aws_vpc.thor-vpc.id

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-public-rt"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_route" "public_internet" {
  count                  = local.nat_enabled ? 1 : 0
  route_table_id         = aws_route_table.public[0].id
  destination_cidr_block = "0.0.0.0/0"
  gateway_id             = aws_internet_gateway.this[0].id
}

resource "aws_route_table_association" "public" {
  count          = length(aws_subnet.public)
  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public[0].id
}

resource "aws_eip" "nat" {
  count  = local.nat_count
  domain = "vpc"

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-nat-eip-${local.azs[count.index]}"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_nat_gateway" "this" {
  count         = local.nat_count
  allocation_id = aws_eip.nat[count.index].id
  subnet_id     = aws_subnet.public[count.index].id

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-nat-${local.azs[count.index]}"
  })

  depends_on = [aws_internet_gateway.this]

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# single: default route on the existing shared private route table.
resource "aws_route" "private_nat" {
  count                  = local.nat_enabled && !local.per_az_nat ? 1 : 0
  route_table_id         = aws_route_table.private.id
  destination_cidr_block = "0.0.0.0/0"
  nat_gateway_id         = aws_nat_gateway.this[0].id
}

# per_az: one private route table per AZ, each routed to its own AZ's NAT.
resource "aws_route_table" "private_az" {
  count  = local.per_az_nat ? length(var.private_subnet_cidrs) : 0
  vpc_id = aws_vpc.thor-vpc.id

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-private-rt-${local.azs[count.index]}"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_route" "private_az_nat" {
  count                  = length(aws_route_table.private_az)
  route_table_id         = aws_route_table.private_az[count.index].id
  destination_cidr_block = "0.0.0.0/0"
  nat_gateway_id         = aws_nat_gateway.this[count.index].id
}

# Attaches the per-AZ route tables to the existing S3 gateway endpoint as separate associations,
# so aws_vpc_endpoint.s3's own route_table_ids stays as-is and S3 traffic never goes through the NAT.
resource "aws_vpc_endpoint_route_table_association" "s3_private_az" {
  count           = var.enable_vpc_endpoints ? length(aws_route_table.private_az) : 0
  vpc_endpoint_id = aws_vpc_endpoint.s3[0].id
  route_table_id  = aws_route_table.private_az[count.index].id
}
