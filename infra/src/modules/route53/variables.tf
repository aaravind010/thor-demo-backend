variable "zones" {
  description = "Hosted zones to manage, keyed by the zone's domain name (e.g. \"dev.example.com\"). Set create_zone = false to look up an existing zone by that name instead of creating one — useful for a parent zone owned/managed elsewhere. parent_zone_name, if set and present as another key in this same map, gets this zone's NS delegation record created automatically in that parent — leave \"\" for a zone with no parent managed here (e.g. the apex itself)."
  type = map(object({
    create_zone      = optional(bool, true)
    comment          = optional(string, "")
    tags             = optional(map(string), {})
    parent_zone_name = optional(string, "")
  }))
  default = {}
}

variable "tags" {
  type        = map(string)
  description = "Tags applied to every zone this module creates, merged with each zone's own tags (Project/Environment/ManagedBy are already applied via provider default_tags)"
  default     = {}
}
