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

variable "egress_allowed_domains" {
  type        = list(string)
  description = "Extra hostnames the VPC may resolve beyond AWS/VPC-internal names (tenant log vendors), DNS Firewall syntax: \"example.com\" and/or \"*.example.com\". Unused when nat_gateway_mode = none"
  default     = []

  validation {
    condition     = !contains(var.egress_allowed_domains, "*")
    error_message = "egress_allowed_domains must not contain \"*\" — that would allow every name and cancel the default deny."
  }
}

variable "dns_firewall_default_action" {
  type        = string
  description = "DNS Firewall action for names not on the allowlist: ALERT (resolve, but log as would-be-blocked) or BLOCK (NXDOMAIN). Start with ALERT; switch to BLOCK once the query logs show no unexpected names"
  default     = "ALERT"

  validation {
    condition     = contains(["ALERT", "BLOCK"], var.dns_firewall_default_action)
    error_message = "dns_firewall_default_action must be one of: ALERT, BLOCK."
  }
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags (Project/Environment/ManagedBy are already applied via provider default_tags)"
  default     = {}
}
