terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

# VPC Link ENIs — egress only, nothing ever connects in. Scoped to just the NLB, not 0.0.0.0/0.
resource "aws_security_group" "vpc_link" {
  name        = "${var.service_name}-${var.environment}-vpclink-sg"
  description = "API Gateway VPC Link ENIs, egress-only"
  vpc_id      = var.vpc_id

  egress {
    description     = "To the NLB"
    from_port       = var.nlb_listener_port
    to_port         = var.nlb_listener_port
    protocol        = "tcp"
    security_groups = [var.nlb_security_group_id]
  }

  tags = merge(var.tags, {
    Name = "${var.service_name}-${var.environment}-vpclink-sg"
  })
}

resource "aws_apigatewayv2_vpc_link" "thor-apigw-vpclink" {
  name               = "${var.service_name}-${var.environment}-vpclink"
  security_group_ids = [aws_security_group.vpc_link.id]
  subnet_ids         = var.private_subnet_ids

  tags = var.tags
}

resource "aws_apigatewayv2_api" "thor-apigw-api" {
  name          = "${var.service_name}-${var.environment}-api"
  protocol_type = "HTTP"

  tags = var.tags
}
