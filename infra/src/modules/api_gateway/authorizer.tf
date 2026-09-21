# REQUEST authorizer (Thor.Authorizer validates ApiKey or Cognito JWT); Host is also an identity source since it resolves the tenant, and Authorization means something different per tenant.
resource "aws_apigatewayv2_authorizer" "thor-api-key" {
  api_id                            = aws_apigatewayv2_api.thor-apigw-api.id
  name                              = "${var.service_name}-${var.environment}-authorizer"
  authorizer_type                   = "REQUEST"
  authorizer_uri                    = var.authorizer_lambda_invoke_arn
  authorizer_payload_format_version = "1.0"
  identity_sources                  = ["$request.header.Authorization", "$request.header.Host"]
  authorizer_result_ttl_in_seconds  = 300
}

# Grants API Gateway permission to invoke the authorizer Lambda.
resource "aws_lambda_permission" "authorizer_invoke" {
  statement_id  = "AllowAPIGatewayInvokeAuthorizer"
  action        = "lambda:InvokeFunction"
  function_name = var.authorizer_lambda_function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.thor-apigw-api.execution_arn}/authorizers/${aws_apigatewayv2_authorizer.thor-api-key.id}"
}
