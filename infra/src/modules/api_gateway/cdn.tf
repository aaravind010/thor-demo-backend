# CloudFront fronts the REST API: hostname, edge TLS, WAF (cdn_waf.tf). Caching is off — this
# is the API's front door, not a content CDN.
#
# No regional custom domain here: api.<env-domain> points at CloudFront or execute-api, never
# both, so a regional domain would just be a redundant origin hostname.
data "aws_region" "current" {}

locals {
  cdn_name_prefix   = "${var.service_name}-${var.environment}-api-cdn"
  cdn_origin_id     = "apigw"
  use_custom_domain = var.domain_name != ""

  # Built from the api id, not parsed from invoke_url — that URL bundles a scheme and
  # stage path that this origin sets as separate args.
  cdn_origin_domain_name = "${aws_apigatewayv2_api.thor-apigw-api.id}.execute-api.${data.aws_region.current.region}.amazonaws.com"
}

# Looked up by name, not hard-coded UUID — self-documenting, and a typo fails at plan time
# instead of silently attaching the wrong policy.
data "aws_cloudfront_cache_policy" "caching_disabled" {
  name = "Managed-CachingDisabled"
}

data "aws_cloudfront_origin_request_policy" "all_viewer_except_host" {
  name = "Managed-AllViewerExceptHostHeader"
}

resource "aws_cloudfront_distribution" "api" {
  enabled     = true
  comment     = local.cdn_name_prefix
  price_class = var.cdn_price_class
  web_acl_id  = aws_wafv2_web_acl.api.arn
  aliases     = local.use_custom_domain ? [var.domain_name] : []

  origin {
    domain_name = local.cdn_origin_domain_name
    origin_id   = local.cdn_origin_id
    # Stage isn't $default, so its name is a real path segment in every invoke URL.
    origin_path = "/${aws_apigatewayv2_stage.thor-apigw-stage.name}"

    custom_origin_config {
      http_port               = 80
      https_port              = 443
      origin_protocol_policy  = "https-only"
      origin_ssl_protocols    = ["TLSv1.2"]
    }
  }

  default_cache_behavior {
    target_origin_id       = local.cdn_origin_id
    viewer_protocol_policy = "redirect-to-https"
    # integration.tf uses ANY, so every CloudFront-forwardable method is allowed here too, plus
    # OPTIONS for preflight. cached_methods stays GET/HEAD — CloudFront allows nothing else there.
    allowed_methods = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods  = ["GET", "HEAD"]
    compress        = true

    # Caching is off, not tuned down: the API is dynamic and per-caller-authorized, so a shared
    # edge cache could leak one caller's response to another.
    cache_policy_id = data.aws_cloudfront_cache_policy.caching_disabled.id

    # Forwards everything except Host — execute-api routes by its own hostname and 403s if it
    # sees the alias instead.
    origin_request_policy_id = data.aws_cloudfront_origin_request_policy.all_viewer_except_host.id
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  # Same either/or pattern as modules/frontend: default certificate or acm_* fields, never both.
  viewer_certificate {
    cloudfront_default_certificate = local.use_custom_domain ? null : true
    acm_certificate_arn            = local.use_custom_domain ? var.acm_certificate_arn : null
    ssl_support_method             = local.use_custom_domain ? "sni-only" : null
    minimum_protocol_version       = local.use_custom_domain ? "TLSv1.2_2021" : null
  }

  tags = merge(var.tags, {
    Name = local.cdn_name_prefix
  })

  # Cloud Custodian auto-tags this post-creation and an SCP blocks removal — ignore tags here.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# CloudFront's hosted_zone_id (Z2FDTNDATAQYW2) is a fixed global constant, as in modules/frontend.
resource "aws_route53_record" "cdn_alias_a" {
  count = local.use_custom_domain ? 1 : 0

  zone_id = var.zone_id
  name    = var.domain_name
  type    = "A"

  alias {
    name                   = aws_cloudfront_distribution.api.domain_name
    zone_id                = aws_cloudfront_distribution.api.hosted_zone_id
    evaluate_target_health = false
  }
}
