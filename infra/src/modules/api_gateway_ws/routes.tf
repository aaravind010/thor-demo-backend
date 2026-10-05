# $connect, $disconnect and $default answer 200 from API Gateway itself: no connection state is
# stored anywhere, so there's nothing to record on connect or clean up on disconnect.
resource "aws_apigatewayv2_integration" "mock" {
  api_id                        = aws_apigatewayv2_api.this.id
  integration_type              = "MOCK"
  template_selection_expression = "\\$default"
  request_templates = {
    "$default" = jsonencode({ statusCode = 200 })
  }
}

resource "aws_apigatewayv2_route" "connect" {
  api_id    = aws_apigatewayv2_api.this.id
  route_key = "$connect"
  target    = "integrations/${aws_apigatewayv2_integration.mock.id}"

  authorization_type = "CUSTOM"
  authorizer_id      = aws_apigatewayv2_authorizer.this.id
}

resource "aws_apigatewayv2_route" "disconnect" {
  api_id    = aws_apigatewayv2_api.this.id
  route_key = "$disconnect"
  target    = "integrations/${aws_apigatewayv2_integration.mock.id}"
}

resource "aws_apigatewayv2_route" "default" {
  api_id    = aws_apigatewayv2_api.this.id
  route_key = "$default"
  target    = "integrations/${aws_apigatewayv2_integration.mock.id}"
}

# {"action":"invoke",...} frames go to Thor.Api through the VPC link. The X-THOR-* headers are set
# here from the $connect authorizer's verified context and the connection id — the client can't set
# headers on a frame. The HTTP API removes or overwrites the same headers (modules/api_gateway
# integration.tf), so Thor.Api can trust them on this endpoint.
#
# With TLS on, the URI names the backend domain so the NLB's ACM cert matches; the VPC link decides
# where the connection actually goes, not DNS.
resource "aws_apigatewayv2_integration" "invoke" {
  api_id = aws_apigatewayv2_api.this.id

  integration_type   = "HTTP_PROXY"
  integration_method = "POST"
  connection_type    = "VPC_LINK"
  connection_id      = aws_api_gateway_vpc_link.this.id
  integration_uri = (var.tls_server_name != ""
    ? "https://${var.tls_server_name}:${var.nlb_listener_port}/v1/intelligence-engine/ws/invoke"
    : "http://${var.nlb_dns_name}:${var.nlb_listener_port}/v1/intelligence-engine/ws/invoke"
  )

  request_parameters = {
    "integration.request.header.X-THOR-TENANT-ID"     = "context.authorizer.tenant_id"
    "integration.request.header.X-THOR-PRINCIPAL-ID"  = "context.authorizer.principal_id"
    "integration.request.header.X-THOR-CONNECTION-ID" = "context.connectionId"
  }
}

resource "aws_apigatewayv2_route" "invoke" {
  api_id    = aws_apigatewayv2_api.this.id
  route_key = "invoke"
  target    = "integrations/${aws_apigatewayv2_integration.invoke.id}"
}
