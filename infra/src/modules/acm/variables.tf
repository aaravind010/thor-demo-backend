variable "certificates" {

  description = "ACM certificates to create, keyed by an arbitrary logical name (e.g. \"dev_frontend\"). A certificate may freely span hostnames from several zones — each validation CNAME is routed to whichever zone in var.zones actually serves it, so there's no per-certificate zone to pick here."
  type = map(object({
    domain_name               = string
    subject_alternative_names = optional(list(string), [])
    # Adds "*.<domain_name>" alongside domain_name. Worth it for a zone apex fronting several
    # hostnames; pointless for a certificate naming one exact host, so it defaults off.
    include_wildcard = optional(bool, false)
    # The region this one certificate is issued in. Required, and per-certificate rather than
    # per-module, because a single environment genuinely needs both: us-east-1 for anything
    # CloudFront serves, the stack's own region for anything an NLB attaches. The caller resolves
    # it (see the root main.tf's "global"/"regional" scope) — this module just honours it.
    #
    # Per-resource region, not a second aliased provider: this resource is for_each'd, and a
    # provider alias can't vary per key, so aliasing would force splitting the module in two.
    region = string
  }))
  default = {}
}

variable "zones" {
  description = "Every hosted zone a certificate's validation records might belong in, as zone name -> zone ID (modules/route53's zone_ids output, passed through whole). Each CNAME goes to the most specific zone that matches it, so a parent and its delegated child can both be present: a record under dev.example.com lands in the dev.example.com zone rather than example.com, where the child's NS delegation would shadow it. Must contain a zone for every domain_name/SAN across var.certificates, or the lookup fails."
  type        = map(string)
  default     = {}
}

variable "tags" {
  type        = map(string)
  description = "Tags applied to every certificate (Project/Environment/ManagedBy are already applied via provider default_tags)"
  default     = {}
}
