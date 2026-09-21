variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod) — also used as the API Gateway stage name"
}

variable "service_name" {
  type        = string
  description = "Name of the service this API Gateway fronts (currently always \"thor-api\", the only publicly-exposed service)"
  default     = "thor-api"
}

variable "nlb_listener_arn" {
  type        = string
  description = "ARN of the NLB's production listener — an HTTP API v2 private integration targets the listener ARN directly, not a DNS-based URI"
}

variable "nlb_security_group_id" {
  type        = string
  description = "NLB's security group ID — the VPC Link's egress is scoped to this, not 0.0.0.0/0"
}

variable "nlb_listener_port" {
  type        = number
  description = "NLB production listener port — matches the VPC Link's egress rule to nlb_security_group_id"
}

variable "vpc_id" {
  type        = string
  description = "VPC the NLB lives in — the VPC Link's ENIs and their security group are provisioned here"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "Subnets the VPC Link's ENIs are placed in — same private subnets the NLB itself uses"
}

variable "authorizer_lambda_invoke_arn" {
  type        = string
  description = "Lambda authorizer's invoke ARN"
}

variable "authorizer_lambda_function_name" {
  type        = string
  description = "Lambda authorizer's function name — used to grant API Gateway invoke permission"
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags"
  default     = {}
}

variable "domain_name" {
  type        = string
    description = "Public hostname for this API, e.g. api.dev.example.com — served by the CloudFront distribution in cdn.tf, which is what owns the name. \"\" (default) leaves the distribution on its own *.cloudfront.net domain: no alias record and no custom certificate."
  default     = ""
}

variable "acm_certificate_arn" {
  type        = string
  description = "ACM certificate covering domain_name — must be in us-east-1, which CloudFront requires of every distribution regardless of which region this API runs in. Required when domain_name is set, unused otherwise."
  default     = ""
}

variable "zone_id" {
  type        = string
  description = "Hosted zone domain_name's alias record gets created in. Required when domain_name is set, unused otherwise."
  default     = ""
}

variable "cdn_price_class" {
  type        = string
  description = "CloudFront price class for this API's distribution — controls which edge locations serve it"
  default     = "PriceClass_100"
}

variable "cdn_waf_rate_limit" {
  type        = number
  description = "WAF rate-limit threshold for this API's distribution: requests from a single IP in a rolling 5-minute window before it's blocked. Tuned separately from the frontend's, since API call rates per client look nothing like browser traffic."
  default     = 2000
}

variable "global_region" {
  type        = string
  description = "Region the CLOUDFRONT-scoped WAFv2 web ACL is created through (us-east-1). Not this module's own region: see cdn_waf.tf."
  default     = ""
}

variable "tls_server_name" {
  type        = string
  description = "Hostname to verify against the NLB listener's cert (its CN/SAN) and send via SNI, for NLB <-> ECS TLS re-encryption. \"\" (default) leaves the integration on plain HTTP, matching the NLB's own default (non-TLS) listener — must agree with whatever set the NLB's cert."
  default     = ""
}
