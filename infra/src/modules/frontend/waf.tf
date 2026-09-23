# A CLOUDFRONT-scoped WAF ACL is global once it exists — enforced at every edge location, not in any
# one region — but it can only be *created* through the us-east-1 endpoint. Any other region rejects
# the request outright ("WAFInvalidParameterException: The scope is not valid., parameter: CLOUDFRONT"),
# so this can't just inherit the provider's region if the stack ever runs somewhere else. There's no
# way around it from the other side either: a CloudFront distribution won't accept a REGIONAL ACL.
#
# Consequence worth knowing: the visibility_config metrics below land in us-east-1 CloudWatch, not
# alongside the rest of this environment's metrics. Alarms and dashboards on them belong there.
resource "aws_wafv2_web_acl" "thor-fe-waf" {
  region = var.global_region

  name        = "${local.name_prefix}-waf"
  description = "WAF for the thor-${var.environment} frontend CloudFront distribution"
  scope       = "CLOUDFRONT"

  default_action {
    allow {}
  }

  # AWS-managed core rule set — common exploits (XSS, path traversal, etc.), same baseline used almost everywhere.
  rule {
    name     = "AWSManagedRulesCommonRuleSet"
    priority = 1

    override_action {
      none {}
    }

    statement {
      managed_rule_group_statement {
        name        = "AWSManagedRulesCommonRuleSet"
        vendor_name = "AWS"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "${local.name_prefix}-common-rule-set"
      sampled_requests_enabled   = true
    }
  }

  # AWS-managed known-bad-inputs rule set — request patterns already known to be malicious.
  rule {
    name     = "AWSManagedRulesKnownBadInputsRuleSet"
    priority = 2

    override_action {
      none {}
    }

    statement {
      managed_rule_group_statement {
        name        = "AWSManagedRulesKnownBadInputsRuleSet"
        vendor_name = "AWS"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "${local.name_prefix}-known-bad-inputs"
      sampled_requests_enabled   = true
    }
  }

  # Basic protection — blocks a single IP sending more than 2000 requests in a rolling 5-minute window.
  rule {
    name     = "rate-limit-per-ip"
    priority = 3

    action {
      block {}
    }

    statement {
      rate_based_statement {
        limit              = 2000
        aggregate_key_type = "IP"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "${local.name_prefix}-rate-limit"
      sampled_requests_enabled   = true
    }
  }

  visibility_config {
    cloudwatch_metrics_enabled = true
    metric_name                = "${local.name_prefix}-waf"
    sampled_requests_enabled   = true
  }

  tags = merge(var.tags, {
    Name = "${local.name_prefix}-waf"
  })

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
