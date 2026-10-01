variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod) — used for resource naming"
}

variable "vpc_cidr" {
  type        = string
  description = "CIDR block for the VPC"
}

variable "az_count" {
  type        = number
  description = "Number of availability zones to span"
  default     = 2
}

variable "private_subnet_cidrs" {
  type        = list(string)
  description = "CIDR blocks for private subnets, one per AZ (ECS Fargate + Aurora/RDS Proxy live here)"
}

variable "enable_vpc_endpoints" {
  type        = bool
  description = "Create gateway/interface VPC endpoints so private subnets never need NAT Gateway egress"
  default     = true
}

variable "nat_gateway_mode" {
  type        = string
  description = "NAT Gateway egress for the private subnets: none, single (one NAT for the VPC), or per_az (one NAT per AZ)"
  default     = "none"

  validation {
    condition     = contains(["none", "single", "per_az"], var.nat_gateway_mode)
    error_message = "nat_gateway_mode must be one of: none, single, per_az."
  }
}

variable "public_subnet_cidrs" {
  type        = list(string)
  description = "CIDR blocks for public subnets, one per AZ (hold only the NAT Gateways). Unused when nat_gateway_mode = none"
  default     = []
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags (Project/Environment/ManagedBy are already applied via provider default_tags)"
  default     = {}
}
