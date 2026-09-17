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

variable "domain_name" {
  type        = string
  description = "Custom hostname to alias this distribution to, e.g. dev.sphereboard.ai. \"\" (default) leaves the distribution on its default *.cloudfront.net domain — no alias record or custom certificate gets configured."
  default     = ""
}

variable "acm_certificate_arn" {
  type        = string
  description = "ACM certificate covering domain_name — must be in us-east-1, same as this module's distribution requires regardless of which region it's deployed from. Required when domain_name is set, unused otherwise."
  default     = ""
}

variable "zone_id" {
  type        = string
  description = "Hosted zone domain_name's alias record gets created in. Required when domain_name is set, unused otherwise."
  default     = ""
}
