output "zone_ids" {
  description = "Map of zone name -> hosted zone ID, for every zone in var.zones (whether created here or looked up)"
  value       = local.zone_ids
}

output "name_servers" {
  description = "Map of zone name -> its 4 name servers. Only populated for zones this module actually created (create_zone = true) — a looked-up zone's name servers are already set at its registrar, so there's nothing to hand anyone."
  value       = { for name, z in aws_route53_zone.this : name => z.name_servers }
}
