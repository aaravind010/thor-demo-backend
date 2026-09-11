variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod)"
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags"
  default     = {}
}
