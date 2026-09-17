# Zones this module creates outright.
resource "aws_route53_zone" "this" {
  for_each = { for name, cfg in var.zones : name => cfg if cfg.create_zone }

  name    = each.key
  comment = each.value.comment

  tags = merge(var.tags, each.value.tags, {
    Name = each.key
  })
}

# Zones that already exist elsewhere (e.g. a parent domain someone else
# owns) — looked up for their zone_id, never created or modified here.
data "aws_route53_zone" "existing" {
  for_each = { for name, cfg in var.zones : name => cfg if !cfg.create_zone }

  name = each.key
}

locals {
  # One combined map of zone name -> zone_id regardless of whether this
  # module created the zone or just looked it up — consumers (like
  # modules/acm) only ever need the zone_id, not which path it came from.
  zone_ids = merge(
    { for name, z in aws_route53_zone.this : name => z.zone_id },
    { for name, z in data.aws_route53_zone.existing : name => z.zone_id }
  )
}
