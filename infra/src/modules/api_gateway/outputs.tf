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
