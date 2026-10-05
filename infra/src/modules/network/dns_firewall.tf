# Route 53 Resolver DNS Firewall. While NAT egress is on, it blocks DNS for public package registries, so nothing in
# the private subnets (apt, pip, NuGet, npm) can download or upgrade packages through the NAT. Every other name still
# resolves, because tenant log vendors can be any hostname. Does not stop connections made to raw IPs.

locals {
  package_registry_domains = [
    # In use today: apt on Ubuntu 24.04 (aspnet:10.0: thor-api, task-api, workflows)
    # and on Debian (python:3.12-slim: intelligence-engine)
    "ubuntu.com", "*.ubuntu.com",
    "debian.org", "*.debian.org",
    # In use today: pip (system pip in python:3.12-slim)
    "pypi.org", "*.pypi.org", "pythonhosted.org", "*.pythonhosted.org",
    # Future-proofing, not used by current runtime images: Ubuntu PPAs, NuGet, npm/yarn
    "*.launchpad.net", "*.launchpadcontent.net",
    "nuget.org", "*.nuget.org",
    "npmjs.org", "*.npmjs.org", "yarnpkg.com", "*.yarnpkg.com",
  ]
}

resource "aws_route53_resolver_firewall_domain_list" "package_registries" {
  count   = local.nat_enabled ? 1 : 0
  name    = "${local.name_prefix}-package-registries"
  domains = local.package_registry_domains

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_route53_resolver_firewall_rule_group" "egress" {
  count = local.nat_enabled ? 1 : 0
  name  = "${local.name_prefix}-dns-egress"

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_route53_resolver_firewall_rule" "block_package_registries" {
  count                   = local.nat_enabled ? 1 : 0
  name                    = "block-package-registries"
  action                  = "BLOCK"
  block_response          = "NXDOMAIN"
  priority                = 100
  firewall_domain_list_id = aws_route53_resolver_firewall_domain_list.package_registries[0].id
  firewall_rule_group_id  = aws_route53_resolver_firewall_rule_group.egress[0].id
}

resource "aws_route53_resolver_firewall_rule_group_association" "egress" {
  count                  = local.nat_enabled ? 1 : 0
  name                   = "${local.name_prefix}-dns-egress"
  firewall_rule_group_id = aws_route53_resolver_firewall_rule_group.egress[0].id
  vpc_id                 = aws_vpc.thor-vpc.id
  priority               = 101

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
