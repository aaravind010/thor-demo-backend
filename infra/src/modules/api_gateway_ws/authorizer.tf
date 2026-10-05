# Same Thor.Authorizer Lambda as the HTTP API, on $connect only — API Gateway keeps the result for
# the life of the connection. Browsers can't set an Authorization header on a WebSocket, so the
# Cognito token arrives as ?token=. It's the only identity source: the tenant comes from the token
# alone (AuthorizerHandler), and a missing token gets a 401 without reaching the Lambda.
resource "aws_apigatewayv2_authorizer" "this" {
  api_id           = aws_apigatewayv2_api.this.id
  name             = "${local.name_prefix}-authorizer"
  authorizer_type  = "REQUEST"
  authorizer_uri   = var.authorizer_lambda_invoke_arn
  identity_sources = ["route.request.querystring.token"]
}

resource "aws_lambda_permission" "authorizer_invoke" {
  statement_id  = "AllowAPIGatewayWebSocketInvokeAuthorizer"
  action        = "lambda:InvokeFunction"
  function_name = var.authorizer_lambda_function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.this.execution_arn}/authorizers/${aws_apigatewayv2_authorizer.this.id}"
}
