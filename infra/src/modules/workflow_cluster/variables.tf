variable "environment" {
  description = "Deployment environment (dev, qa, prod)."
  type        = string
}

variable "enable_container_insights" {
  description = "Enable ECS Container Insights on the shared workflow cluster."
  type        = bool
  default     = false
}

variable "tags" {
  description = "Tags applied to every resource in this module."
  type        = map(string)
  default     = {}
}
