variable "certificates" {
  description = "ACM certificates to create, keyed by an arbitrary logical name (e.g. \"dev_frontend\"). zone_id must be the hosted zone that domain_name and every entry in subject_alternative_names actually belong to — every validation CNAME for a given certificate is written into that one zone, so a certificate can't span hostnames from two different zones."
  type = map(object({
    domain_name               = string
    subject_alternative_names = optional(list(string), [])
    zone_id                   = string
  }))
  default = {}
}

variable "tags" {
  type        = map(string)
  description = "Tags applied to every certificate (Project/Environment/ManagedBy are already applied via provider default_tags)"
  default     = {}
}
