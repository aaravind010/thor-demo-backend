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
  description = "Custom hostname to map this API to, e.g. api.dev.cndemo.com. \"\" (default) leaves the API reachable only via its default execute-api URL — no custom domain, mapping, or alias records get created."
  default     = ""
}

variable "acm_certificate_arn" {
  type        = string
  description = "ACM certificate covering domain_name — must be REGIONAL (same region as this API), not the us-east-1-only certs CloudFront requires, unless this API also happens to run in us-east-1. Required when domain_name is set, unused otherwise."
  default     = ""
}

variable "zone_id" {
  type        = string
  description = "Hosted zone domain_name's alias records get created in. Required when domain_name is set, unused otherwise."
  default     = ""
}

variable "tls_server_name" {
  type        = string
  description = "Hostname to verify against the NLB listener's cert (its CN/SAN) and send via SNI, for NLB <-> ECS TLS re-encryption. \"\" (default) leaves the integration on plain HTTP, matching the NLB's own default (non-TLS) listener — must agree with whatever set the NLB's cert."
  default     = ""
}
