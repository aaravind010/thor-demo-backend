# Catch-all proxy — forwards every path/method through the VPC Link to the NLB, which forwards to
# thor. The stage (deployment.tf) is named, not $default, so it's a real leading path segment on
# every request that reaches this API — including through CloudFront, whose origin_path (cdn.tf)
# prepends it to every request regardless of environment. thor.api's own routing knows nothing
# about API Gateway stages, so request_parameters rewrites the outgoing path to strip it before
# forwarding (e.g. "/dev/v1/orders" -> "/v1/orders").
#
# Two integrations, not one, because the rewrite expression differs per route: the proxy route's
# $request.path.proxy only resolves against its own {proxy+} capture, and referencing it from the
# root route (which has no such parameter) is invalid.

resource "aws_apigatewayv2_integration" "proxy" {
  api_id = aws_apigatewayv2_api.thor-apigw-api.id

  integration_type       = "HTTP_PROXY"
  integration_method     = "ANY"
  connection_type        = "VPC_LINK"
  connection_id          = aws_apigatewayv2_vpc_link.thor-apigw-vpclink.id
  integration_uri        = var.nlb_listener_arn
  payload_format_version = "1.0"

  # X-THOR-TENANT-ID and X-THOR-CALLER-GROUPS come from the authorizer's verified context, never
  # the client — overwrite replaces any value the caller sent. Thor.Api's admin gate trusts the
  # groups header (RequireTenantAdminAttribute).
  request_parameters = {
    "overwrite:path"                        = "/$request.path.proxy"
    "overwrite:header.X-THOR-TENANT-ID"     = "$context.authorizer.tenant_id"
    "overwrite:header.X-THOR-CALLER-GROUPS" = "$context.authorizer.groups"
  }

  # Must agree with the NLB's own TLS state — plain HTTP unless a real cert is configured there too.
  dynamic "tls_config" {
    for_each = var.tls_server_name != "" ? [1] : []
    content {
      server_name_to_verify = var.tls_server_name
    }
  }
}

resource "aws_apigatewayv2_integration" "proxy_root" {
  api_id = aws_apigatewayv2_api.thor-apigw-api.id

  integration_type       = "HTTP_PROXY"
  integration_method     = "ANY"
  connection_type        = "VPC_LINK"
  connection_id          = aws_apigatewayv2_vpc_link.thor-apigw-vpclink.id
  integration_uri        = var.nlb_listener_arn
  payload_format_version = "1.0"

  request_parameters = {
    "overwrite:path"                        = "/"
    "overwrite:header.X-THOR-TENANT-ID"     = "$context.authorizer.tenant_id"
    "overwrite:header.X-THOR-CALLER-GROUPS" = "$context.authorizer.groups"
  }

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
  target    = "integrations/${aws_apigatewayv2_integration.proxy_root.id}"

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

# Login discovery: the SPA calls this before sign-in, so there's no Authorization header yet —
# behind the authorizer it would 401 at API Gateway. It only returns a tenant's public Cognito
# pool/client ids (Thor.Api LoginConfigController). Its own integration because there's no
# authorizer context to map on a NONE route; the tenant/groups headers are removed instead so a
# caller-supplied value can never reach Thor.Api through this route.
resource "aws_apigatewayv2_integration" "login_config" {
  api_id = aws_apigatewayv2_api.thor-apigw-api.id

  integration_type       = "HTTP_PROXY"
  integration_method     = "GET"
  connection_type        = "VPC_LINK"
  connection_id          = aws_apigatewayv2_vpc_link.thor-apigw-vpclink.id
  integration_uri        = var.nlb_listener_arn
  payload_format_version = "1.0"

  request_parameters = {
    "overwrite:path"                     = "/v1/login-config/$request.path.subdomain"
    "remove:header.X-THOR-TENANT-ID"     = "''"
    "remove:header.X-THOR-CALLER-GROUPS" = "''"
  }

  dynamic "tls_config" {
    for_each = var.tls_server_name != "" ? [1] : []
    content {
      server_name_to_verify = var.tls_server_name
    }
  }
}

resource "aws_apigatewayv2_route" "login_config" {
  api_id    = aws_apigatewayv2_api.thor-apigw-api.id
  route_key = "GET /v1/login-config/{subdomain}"
  target    = "integrations/${aws_apigatewayv2_integration.login_config.id}"

  authorization_type = "NONE"
}

# TEMPORARY, UNAUTHENTICATED, dev only: connector API-key creation until Cognito sign-in is live.
# The authorizer exempts this route (ExemptRoutes.cs), so there's no authorizer tenant_id to map —
# unlike the proxy integrations, X-THOR-TENANT-ID is NOT overwritten and the caller's value reaches
# Thor.Api as-is. Anyone who can reach the dev stage can mint a connector key for any tenant id.
# Remove this integration + route together with the authorizer and Thor.Api exemptions.
resource "aws_apigatewayv2_integration" "connector_api_keys" {
  count = var.environment == "dev" ? 1 : 0

  api_id = aws_apigatewayv2_api.thor-apigw-api.id

  integration_type       = "HTTP_PROXY"
  integration_method     = "POST"
  connection_type        = "VPC_LINK"
  connection_id          = aws_apigatewayv2_vpc_link.thor-apigw-vpclink.id
  integration_uri        = var.nlb_listener_arn
  payload_format_version = "1.0"

  request_parameters = {
    "overwrite:path"                     = "/v1/connector-api-keys"
    "remove:header.X-THOR-CALLER-GROUPS" = "''"
  }

  dynamic "tls_config" {
    for_each = var.tls_server_name != "" ? [1] : []
    content {
      server_name_to_verify = var.tls_server_name
    }
  }
}

resource "aws_apigatewayv2_route" "connector_api_keys" {
  count = var.environment == "dev" ? 1 : 0

  api_id    = aws_apigatewayv2_api.thor-apigw-api.id
  route_key = "POST /v1/connector-api-keys"
  target    = "integrations/${aws_apigatewayv2_integration.connector_api_keys[0].id}"

  # Kept behind the authorizer so ExemptRoutes.cs stays the one place deciding what's exempt.
  authorization_type = "CUSTOM"
  authorizer_id      = aws_apigatewayv2_authorizer.thor-api-key.id
}
