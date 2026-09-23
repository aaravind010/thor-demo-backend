# DNS validation only — email validation can't be automated, which defeats
# the point of this module.
#
# Certificates here span two regions (each entry carries its own; see variables.tf). The Route53
# records that prove them do not: Route53 is global, so one zone validates certificates in any
# region, and nothing below needs a region argument.
resource "aws_acm_certificate" "this" {
  for_each = var.certificates

  # Per-certificate, since one environment needs certificates in two different regions at once —
  # us-east-1 for CloudFront's, the stack's own region for the NLB's. Route53 is global, so the
  # validation records below are unaffected by this and cross-region validation just works.
  region = each.value.region

  domain_name = each.value.domain_name
  # Wildcard is opt-in per certificate, not automatic: a cert for one exact hostname
  # (api.dev.example.com) has no use for *.api.dev.example.com, and requesting it would just
  # add a second validation record for nothing.
  subject_alternative_names = concat(
    each.value.include_wildcard ? ["*.${each.value.domain_name}"] : [],
    each.value.subject_alternative_names,
  )
  validation_method = "DNS"

  # Cloud Custodian auto-tags this post-creation and an SCP blocks removal — ignore tags here.
  lifecycle {
    create_before_destroy = true
    ignore_changes        = [tags, tags_all]
  }

  tags = merge(var.tags, {
    Name = each.key
  })
}

locals {
  # Zone names ordered most-specific-first, so a delegated child zone beats its parent — e.g. a
  # record under dev.example.com belongs there, not in example.com, where the child's NS
  # delegation would shadow it. Label count is zero-padded into the sort key since sort() is
  # lexical, not by length.
  zones_by_specificity = [
    for entry in reverse(sort([
      for zone_name in keys(var.zones) :
      format("%03d|%s", length(split(".", zone_name)), zone_name)
    ])) : split("|", entry)[1]
  ]

  # Flattens (certificate -> domain_validation_options) into one map, one entry per hostname
  # needing a CNAME. Keyed "<cert_key>/<hostname>" so for_each never collides, even when two
  # certs share a hostname across SANs. Keyed on domain_name, not record name: for_each keys
  # must be known at plan time, and resource_record_name isn't until the cert exists.
  #
  # A domain and its wildcard (example.com / *.example.com) share one validation record, so two
  # entries can write the identical CNAME — harmless, and what allow_overwrite is for.
  validation_records = merge([
    for cert_key, cert in aws_acm_certificate.this : {
      for dvo in cert.domain_validation_options : "${cert_key}/${dvo.domain_name}" => {
        cert_key = cert_key
        # The zone that actually serves this record, not one zone per certificate — a cert may
        # span an apex and a delegated child (example.com + *.dev.example.com), whose records
        # land in different zones. Wildcards are stripped first, since "*.dev.example.com" is
        # proven via "_hash.dev.example.com" and belongs wherever dev.example.com does.
        zone_id = var.zones[[
          for zone_name in local.zones_by_specificity : zone_name
          if trimprefix(dvo.domain_name, "*.") == zone_name
          || endswith(trimprefix(dvo.domain_name, "*."), ".${zone_name}")
        ][0]]
        name   = dvo.resource_record_name
        type   = dvo.resource_record_type
        record = dvo.resource_record_value
      }
    }
  ]...)
}

resource "aws_route53_record" "validation" {
  for_each = local.validation_records

  zone_id         = each.value.zone_id
  name            = each.value.name
  type            = each.value.type
  records         = [each.value.record]
  ttl             = 60
  allow_overwrite = true
}

resource "aws_acm_certificate_validation" "this" {
  for_each = aws_acm_certificate.this

  # Has to be issued against the same regional ACM endpoint that holds the certificate, or the
  # validation call goes looking for an ARN the endpoint has never heard of.
  region = var.certificates[each.key].region

  certificate_arn = each.value.arn

  # Only this certificate's validation records — filtered back out of the flattened map by
  # the cert_key each entry was tagged with.
  validation_record_fqdns = [
    for key, record in local.validation_records :
    aws_route53_record.validation[key].fqdn
    if record.cert_key == each.key
  ]
}
