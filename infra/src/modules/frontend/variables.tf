variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod) — used for resource naming"
}

variable "price_class" {
  type        = string
  description = "CloudFront price class — controls which edge locations serve this distribution"
  default     = "PriceClass_100"
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags (Project/Environment/ManagedBy are already applied via provider default_tags)"
  default     = {}
}
