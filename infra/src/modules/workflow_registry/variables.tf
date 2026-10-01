variable "workflows" {
  description = "Workflow names to create a repository for, e.g. [\"ingestion\", \"atre\"]. A plain list of names — this module must never depend on anything a workflow creates."
  type        = set(string)
}

variable "environment" {
  description = "Deployment environment (dev, qa, prod)."
  type        = string
}

variable "deploy_tag" {
  description = "The one tag CI is allowed to move, excluded from immutability. Must match modules/workflow's image_tag — they are two halves of the same contract: Terraform points the compute at this tag, and CI repoints the tag at each new build."
  type        = string
  default     = "deployed"
}

variable "tags" {
  description = "Tags applied to every resource in this module."
  type        = map(string)
  default     = {}
}
