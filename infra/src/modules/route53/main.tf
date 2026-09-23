# Zones created by this module.
resource "aws_route53_zone" "this" {
  for_each = { for name, cfg in var.zones : name => cfg if cfg.create_zone }

  name    = each.key
  comment = each.value.comment

  tags = merge(var.tags, each.value.tags, {
    Name = each.key
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# Zones that already exist elsewhere (e.g. a parent domain someone else
# owns) — looked up for their zone_id, never created or modified here.
data "aws_route53_zone" "existing" {
  for_each = { for name, cfg in var.zones : name => cfg if !cfg.create_zone }

  name = each.key
}

locals {
  # Combined zone name -> zone_id map, created or looked-up alike, since
  # consumers (e.g. modules/acm) only care about the zone_id.
  zone_ids = merge(
    { for name, z in aws_route53_zone.this : name => z.zone_id },
    { for name, z in data.aws_route53_zone.existing : name => z.zone_id }
  )
}

# NS delegation record in the parent zone, for every created zone whose
# parent is also in var.zones (created or looked-up, either way its
# zone_id is in local.zone_ids). Avoids manually pasting name servers into
# the parent, which Terraform can't see, never cleans up, and which then
# blocks parent zone deletion (HostedZoneNotEmpty).
resource "aws_route53_record" "delegation" {
  for_each = {
    for name, cfg in var.zones : name => cfg
    if cfg.create_zone && cfg.parent_zone_name != "" && contains(keys(var.zones), cfg.parent_zone_name)
  }

  zone_id = local.zone_ids[each.value.parent_zone_name]
  name    = each.key
  type    = "NS"
  ttl     = 300
  records = aws_route53_zone.this[each.key].name_servers
}
