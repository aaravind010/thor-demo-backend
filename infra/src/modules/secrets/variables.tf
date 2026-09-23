variable "environment" {
  type        = string
  description = "Environment name (dev, qa, prod)"
}

variable "tags" {
  type        = map(string)
  description = "Additional resource-specific tags"
  default     = {}
}

variable "recovery_window_in_days" {
  type        = number
  description = "Days a deleted secret stays recoverable before purging for good. 0 = delete immediately."
  default     = 30
}
