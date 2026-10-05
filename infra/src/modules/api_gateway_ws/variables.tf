variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod) — also used as the WebSocket API stage name"
}

variable "service_name" {
  type        = string
  description = "Name of the service this API fronts (currently always \"thor-api\")"
  default     = "thor-api"
}

variable "nlb_arn" {
  type        = string
  description = "ARN of thor-api's NLB — a REST-style VPC link targets the load balancer itself, not a listener"
}

variable "nlb_dns_name" {
  type        = string
  description = "NLB's DNS name — the integration URI host when tls_server_name is \"\""
}

variable "nlb_listener_port" {
  type        = number
  description = "NLB production listener port the integration URI targets"
}

variable "tls_server_name" {
  type        = string
  description = "Hostname on the NLB listener's cert. Set: the integration calls https://<this>, so the cert matches. \"\" (default): plain http to the NLB's DNS name. Must agree with the NLB's own TLS state, like modules/api_gateway's variable of the same name."
  default     = ""
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
