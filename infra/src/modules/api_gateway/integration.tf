# Catch-all proxy — forwards every path/method through the VPC Link to the NLB, which forwards to thor.

resource "aws_apigatewayv2_integration" "proxy" {
  api_id = aws_apigatewayv2_api.thor-apigw-api.id

  integration_type       = "HTTP_PROXY"
  integration_method     = "ANY"
  connection_type        = "VPC_LINK"
  connection_id          = aws_apigatewayv2_vpc_link.thor-apigw-vpclink.id
  integration_uri        = var.nlb_listener_arn
  payload_format_version = "1.0"

  # Must agree with the NLB's own TLS state — plain HTTP unless a real cert is configured there too.
  dynamic "tls_config" {
    for_each = var.tls_server_name != "" ? [1] : []
    content {
      server_name_to_verify = var.tls_server_name
    }
  }
}

resource "aws_apigatewayv2_route" "proxy_root" {
  api_id    = aws_apigatewayv2_api.thor-apigw-api.id
  route_key = "ANY /"
  target    = "integrations/${aws_apigatewayv2_integration.proxy.id}"

  authorization_type = "CUSTOM"
  authorizer_id      = aws_apigatewayv2_authorizer.thor-api-key.id
}

resource "aws_apigatewayv2_route" "proxy" {
  api_id    = aws_apigatewayv2_api.thor-apigw-api.id
  route_key = "ANY /{proxy+}"
  target    = "integrations/${aws_apigatewayv2_integration.proxy.id}"

  authorization_type = "CUSTOM"
  authorizer_id      = aws_apigatewayv2_authorizer.thor-api-key.id
}
