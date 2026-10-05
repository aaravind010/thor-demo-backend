terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

data "aws_region" "current" {}

locals {
  name_prefix = "${var.service_name}-${var.environment}-ws"
}

# WebSocket API for streamed agent responses. API Gateway holds each client socket: every client
# frame becomes one request to Thor.Api (routes.tf), and Thor.Api pushes replies back through the
# @connections management API, over the NAT (modules/network/nat.tf). Deliberately no execute-api
# interface endpoint — with private DNS it would capture those @connections calls and break them.
resource "aws_apigatewayv2_api" "this" {
  name                       = "${local.name_prefix}-api"
  protocol_type              = "WEBSOCKET"
  route_selection_expression = "$request.body.action"

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# No access logs yet: WebSocket stages need the account-level API Gateway CloudWatch role, which
# isn't managed here. When added, the log format must not include the query string — the token is in it.
resource "aws_apigatewayv2_stage" "this" {
  api_id      = aws_apigatewayv2_api.this.id
  name        = var.environment
  auto_deploy = true

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# REST-style (v1) VPC link: WebSocket APIs can't use the HTTP API's v2 VPC link. It targets the NLB
# itself (via PrivateLink), not a listener — the NLB SG's VPC-CIDR ingress rule already admits it.
resource "aws_api_gateway_vpc_link" "this" {
  name        = "${local.name_prefix}-vpclink"
  target_arns = [var.nlb_arn]

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
