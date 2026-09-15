# Zones this module creates outright.
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
  # One combined map of zone name -> zone_id regardless of whether this
  # module created the zone or just looked it up — consumers (like
  # modules/acm) only ever need the zone_id, not which path it came from.
  zone_ids = merge(
    { for name, z in aws_route53_zone.this : name => z.zone_id },
    { for name, z in data.aws_route53_zone.existing : name => z.zone_id }
  )
}

# NS delegation record in the parent, for every zone this module created that
# names a parent also present in var.zones (regardless of whether the parent
# itself was created here or just looked up — either way its zone_id is in
# local.zone_ids). Replaces manually pasting the child's 4 name servers into
# the parent zone: that manual record is invisible to Terraform, so it's
# never cleaned up on destroy and blocks deleting the parent zone
# (HostedZoneNotEmpty) — this resource fixes that going forward.
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
