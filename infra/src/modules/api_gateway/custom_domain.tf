# REGIONAL, not EDGE — the certificate must live in this API's own region.
# Thor runs everything in us-east-1 today, same region the CloudFront certs
# already use, so this isn't extra complexity in practice; it would matter
# only if this API ever moved to a different region than its certs.
resource "aws_apigatewayv2_domain_name" "thor-apigw-domain" {
  count       = var.domain_name != "" ? 1 : 0
  domain_name = var.domain_name

  domain_name_configuration {
    certificate_arn = var.acm_certificate_arn
    endpoint_type   = "REGIONAL"
    security_policy = "TLS_1_2"
  }

  tags = var.tags
}

# No api_mapping_key — mapped at the domain's root, not under a path prefix.
resource "aws_apigatewayv2_api_mapping" "thor-apigw-mapping" {
  count = var.domain_name != "" ? 1 : 0

  api_id      = aws_apigatewayv2_api.thor-apigw-api.id
  domain_name = aws_apigatewayv2_domain_name.thor-apigw-domain[0].id
  stage       = aws_apigatewayv2_stage.thor-apigw-stage.id
}

# A + AAAA — API Gateway regional custom domains serve both by default, so
# an IPv6-only client needs the AAAA to actually resolve this hostname.
resource "aws_route53_record" "thor-apigw-alias-a" {
  count = var.domain_name != "" ? 1 : 0

  zone_id = var.zone_id
  name    = var.domain_name
  type    = "A"

  alias {
    name                   = aws_apigatewayv2_domain_name.thor-apigw-domain[0].domain_name_configuration[0].target_domain_name
    zone_id                = aws_apigatewayv2_domain_name.thor-apigw-domain[0].domain_name_configuration[0].hosted_zone_id
    evaluate_target_health = false
  }
}
