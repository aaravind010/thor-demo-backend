# Route 53 Resolver DNS Firewall — default deny for the whole VPC while NAT egress is on. Rules, first match wins:
#   100  BLOCK  package registries — always, even if one is later added to the allowlist by mistake
#   200  ALLOW  AWS + VPC-internal names (everything ECS, workflows and Lambdas call today) + var.egress_allowed_domains
#   300  var.dns_firewall_default_action for everything else — ALERT logs would-be blocks only, BLOCK answers NXDOMAIN
# The firewall is VPC-wide (it can't be scoped per subnet or service). Does not stop connections made to raw IPs.

locals {
  allowed_domains = concat(
    # ECR, Secrets Manager, CloudWatch Logs, S3, Cognito, RDS Proxy, Neptune, Step Functions, Bedrock; EC2-internal hostnames.
    ["amazonaws.com", "*.amazonaws.com", "*.api.aws", "*.internal"],
    var.egress_allowed_domains,
  )

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

resource "aws_route53_resolver_firewall_domain_list" "allowed" {
  count   = local.nat_enabled ? 1 : 0
  name    = "${local.name_prefix}-dns-allowed"
  domains = local.allowed_domains

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_route53_resolver_firewall_domain_list" "everything" {
  count   = local.nat_enabled ? 1 : 0
  name    = "${local.name_prefix}-dns-everything"
  domains = ["*"]

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_route53_resolver_firewall_rule" "allow_listed" {
  count                   = local.nat_enabled ? 1 : 0
  name                    = "allow-listed"
  action                  = "ALLOW"
  priority                = 200
  firewall_domain_list_id = aws_route53_resolver_firewall_domain_list.allowed[0].id
  firewall_rule_group_id  = aws_route53_resolver_firewall_rule_group.egress[0].id

  # Check only the name asked for, not its CNAME chain — otherwise every CDN target behind an allowed name (e.g.
  # vendor → *.cloudfront.net) would also need listing. The registry BLOCK above still inspects the full chain.
  firewall_domain_redirection_action = "TRUST_REDIRECTION_DOMAIN"
}

resource "aws_route53_resolver_firewall_rule" "default_deny" {
  count                   = local.nat_enabled ? 1 : 0
  name                    = "default-deny"
  action                  = var.dns_firewall_default_action
  block_response          = var.dns_firewall_default_action == "BLOCK" ? "NXDOMAIN" : null
  priority                = 300
  firewall_domain_list_id = aws_route53_resolver_firewall_domain_list.everything[0].id
  firewall_rule_group_id  = aws_route53_resolver_firewall_rule_group.egress[0].id
}

# Query logging — the only place ALERT/BLOCK matches are recorded by name and source IP (CloudWatch metrics carry counts only).
resource "aws_cloudwatch_log_group" "dns_query_logs" {
  count             = local.nat_enabled ? 1 : 0
  name              = "/aws/route53resolver/${local.name_prefix}"
  retention_in_days = 30

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_route53_resolver_query_log_config" "this" {
  count           = local.nat_enabled ? 1 : 0
  name            = "${local.name_prefix}-dns-query-logs"
  destination_arn = aws_cloudwatch_log_group.dns_query_logs[0].arn

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_route53_resolver_query_log_config_association" "this" {
  count                        = local.nat_enabled ? 1 : 0
  resolver_query_log_config_id = aws_route53_resolver_query_log_config.this[0].id
  resource_id                  = aws_vpc.thor-vpc.id
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
