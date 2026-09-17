# REGIONAL, not EDGE — the certificate must live in this API's own region, same as the CloudFront certs.
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

# A record — aliases the custom domain to API Gateway's regional target domain name so it resolves.
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
