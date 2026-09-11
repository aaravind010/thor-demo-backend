# auto_deploy redeploys automatically on any route/integration change.
resource "aws_apigatewayv2_stage" "thor-apigw-stage" {
  api_id      = aws_apigatewayv2_api.thor-apigw-api.id
  name        = var.environment
  auto_deploy = true

  tags = var.tags
}
