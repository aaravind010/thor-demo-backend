output "api_id" {
  value = aws_apigatewayv2_api.thor-apigw-api.id
}

output "vpc_link_id" {
  value = aws_apigatewayv2_vpc_link.thor-apigw-vpclink.id
}

output "stage_name" {
  value = aws_apigatewayv2_stage.thor-apigw-stage.name
}

output "invoke_url" {
  description = "Default execute-api invoke URL — no custom domain yet"
  value       = aws_apigatewayv2_stage.thor-apigw-stage.invoke_url
}

output "cdn_distribution_id" {
  description = "CloudFront distribution ID — needed to create invalidations"
  value       = aws_cloudfront_distribution.api.id
}

output "cdn_distribution_domain_name" {
  description = "The distribution's own *.cloudfront.net hostname"
  value       = aws_cloudfront_distribution.api.domain_name
}

output "cdn_web_acl_arn" {
  value = aws_wafv2_web_acl.api.arn
}
