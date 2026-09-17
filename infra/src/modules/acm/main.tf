# Issues one certificate per configured domain (frontend, api_gateway, NLB backend TLS).
resource "aws_acm_certificate" "this" {
  for_each = var.certificates

  domain_name               = each.value.domain_name
  subject_alternative_names = each.value.subject_alternative_names
  validation_method         = "DNS"

  tags = merge(var.tags, {
    Name = each.key
  })
}

locals {
  # One entry per hostname needing a validation CNAME, keyed by "<cert_key>/<hostname>" to avoid collisions across certs.
  validation_records = merge([
    for cert_key, cert in aws_acm_certificate.this : {
      for dvo in cert.domain_validation_options : "${cert_key}/${dvo.domain_name}" => {
        cert_key = cert_key
        zone_id  = var.certificates[cert_key].zone_id
        name     = dvo.resource_record_name
        type     = dvo.resource_record_type
        record   = dvo.resource_record_value
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

  certificate_arn = each.value.arn

  # Filters the flattened map back down to just this certificate's records, by cert_key.
  validation_record_fqdns = [
    for key, record in local.validation_records :
    aws_route53_record.validation[key].fqdn
    if record.cert_key == each.key
  ]
}
