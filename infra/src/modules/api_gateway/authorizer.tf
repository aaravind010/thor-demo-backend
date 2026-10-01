# REQUEST authorizer (Thor.Authorizer validates ApiKey or Cognito JWT). Identity sources are the
# cache key, so they must cover every input the decision depends on:
# - Authorization, which is also the only source of the tenant (AuthorizerHandler): no Host or
#   other header is read, so the cached decision can't be replayed for a different tenant.
#   Requests without it get a 401 from API Gateway, never reaching the Lambda.
# - httpMethod + path, because exempt routes (ExemptRoutes.cs) are allowed without a credential;
#   without them a cached exempt Allow (Resource /*) would be replayed on every other route.
resource "aws_apigatewayv2_authorizer" "thor-api-key" {
  api_id                            = aws_apigatewayv2_api.thor-apigw-api.id
  name                              = "${var.service_name}-${var.environment}-authorizer"
  authorizer_type                   = "REQUEST"
  authorizer_uri                    = var.authorizer_lambda_invoke_arn
  authorizer_payload_format_version = "1.0"
  identity_sources = [
    "$request.header.Authorization",
    "$context.httpMethod",
    "$context.path",
  ]
  # Short TTL bounds how long a Deny from a transient failure (or a revoked credential) is replayed.
  authorizer_result_ttl_in_seconds = 60
}

# Grants API Gateway permission to invoke the authorizer Lambda.
resource "aws_lambda_permission" "authorizer_invoke" {
  statement_id  = "AllowAPIGatewayInvokeAuthorizer"
  action        = "lambda:InvokeFunction"
  function_name = var.authorizer_lambda_function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.thor-apigw-api.execution_arn}/authorizers/${aws_apigatewayv2_authorizer.thor-api-key.id}"
}
