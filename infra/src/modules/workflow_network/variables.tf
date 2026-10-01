variable "workflows" {
  description = "Workflow names to create a security group for. A plain list of names — this module must never depend on anything a workflow creates, or the platform/workflow state cycle comes back."
  type        = set(string)
}

variable "environment" {
  description = "Deployment environment (dev, qa, prod)."
  type        = string
}

variable "vpc_id" {
  description = "VPC the workflow compute runs in."
  type        = string
}

variable "tags" {
  description = "Tags applied to every resource in this module."
  type        = map(string)
  default     = {}
}
