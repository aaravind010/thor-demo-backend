locals {
  public_services = { for k, v in local.active_services : k => v if v.expose_via_nlb }

  nlb_tls_enabled = var.nlb_certificate_arn != ""

}

# Blue/primary target group. Green (below) and the production listener exist unconditionally for every publicly-exposed service regardless of its current deployment_strategy — toggling ROLLING <-> BLUE_GREEN is a per-deploy choice, not something that should tear down load balancer infrastructure.
resource "aws_lb_target_group" "thor-nlb-tg-blue" {
  for_each = local.public_services

  name        = local.name_prefix[each.key]
  port        = each.value.container_port
  protocol    = local.nlb_tls_enabled ? "TLS" : "TCP"
  vpc_id      = var.vpc_id
  target_type = "ip"

  health_check {
    # HTTPS when TLS is on — safe since the NLB never validates the target's cert.
    protocol            = local.nlb_tls_enabled ? "HTTPS" : "HTTP"
    path                = each.value.health_check_path
    healthy_threshold   = 3
    unhealthy_threshold = 3
    interval            = 30
  }

  tags = var.tags
}

resource "aws_lb_target_group" "thor-nlb-tg-green" {
  for_each = local.public_services

  name        = "${local.name_prefix[each.key]}-green"
  port        = each.value.container_port
  protocol    = local.nlb_tls_enabled ? "TLS" : "TCP"
  vpc_id      = var.vpc_id
  target_type = "ip"

  health_check {
    protocol            = local.nlb_tls_enabled ? "HTTPS" : "HTTP"
    path                = each.value.health_check_path
    healthy_threshold   = 3
    unhealthy_threshold = 3
    interval            = 30
  }

  tags = var.tags
}

# Attached at creation only — AWS doesn't allow adding a security group to an existing NLB, so this forces a destroy+recreate of aws_lb.thor-nlb on first apply. Ingress rule lives in security_groups.tf (nlb_from_vpc), not inline, to match the service SGs' pattern.
resource "aws_security_group" "nlb" {
  for_each = local.public_services

  name        = "${local.name_prefix[each.key]}-nlb-sg"
  description = "NLB ingress restricted to the rule in security_groups.tf, egress open (matches service SGs pattern)"
  vpc_id      = var.vpc_id

  egress {
    description = "All traffic"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(var.tags, {
    Name = "${local.name_prefix[each.key]}-nlb-sg"
  })
}

# Internal — reached via VPC Link from API Gateway (see the architecture doc), not directly from the internet.
resource "aws_lb" "thor-nlb" {
  for_each = local.public_services

  name               = "thor-nlb-${var.environment}"
  load_balancer_type = "network"
  internal           = true
  subnets            = var.private_subnet_ids
  security_groups    = [aws_security_group.nlb[each.key].id]

  tags = merge(var.tags, {
    Name = "thor-nlb-${var.environment}"
  })
}

# ECS modifies this listener's default_action directly during a blue/green deployment, so its own state must not fight that.
resource "aws_lb_listener" "thor-nlb-listener" {
  for_each = local.public_services

  load_balancer_arn = aws_lb.thor-nlb[each.key].arn
  port              = each.value.nlb_listener_port
  protocol          = local.nlb_tls_enabled ? "TLS" : "TCP"
  certificate_arn   = local.nlb_tls_enabled ? var.nlb_certificate_arn : null
  ssl_policy        = local.nlb_tls_enabled ? "ELBSecurityPolicy-TLS13-1-2-2021-06" : null

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.thor-nlb-tg-blue[each.key].arn
  }

  lifecycle {
    ignore_changes = [default_action]
  }

  tags = var.tags
}

# Test-traffic listener for blue/green cutover validation — always created, even for ROLLING services, so toggling
# deployment_strategy never tears down the LB. ECS only wires it in via advanced_configuration when BLUE_GREEN.
resource "aws_lb_listener" "thor-nlb-test-listener" {
  for_each = local.public_services

  load_balancer_arn = aws_lb.thor-nlb[each.key].arn
  port              = each.value.nlb_test_listener_port
  protocol          = local.nlb_tls_enabled ? "TLS" : "TCP"
  certificate_arn   = local.nlb_tls_enabled ? var.nlb_certificate_arn : null
  ssl_policy        = local.nlb_tls_enabled ? "ELBSecurityPolicy-TLS13-1-2-2021-06" : null

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.thor-nlb-tg-blue[each.key].arn
  }

  lifecycle {
    ignore_changes = [default_action]
  }

  tags = var.tags
}

# Trusted by ecs.amazonaws.com (the ECS control plane modifying the listener), not ecs-tasks.amazonaws.com like the execution/task roles in iam.tf.
data "aws_iam_policy_document" "ecs_service_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "blue_green" {
  for_each = local.public_services

  name                 = "${local.name_prefix[each.key]}-bluegreen"
  assume_role_policy   = data.aws_iam_policy_document.ecs_service_assume.json
  permissions_boundary = var.iam_permissions_boundary_arn
  tags                 = var.tags
}

data "aws_iam_policy_document" "blue_green_permissions" {
  for_each = local.public_services

  statement {
    actions = [
      "elasticloadbalancing:DescribeTargetGroups",
      "elasticloadbalancing:DescribeListeners",
      "elasticloadbalancing:DescribeTargetHealth",
      "elasticloadbalancing:ModifyListener",
      "elasticloadbalancing:RegisterTargets",
      "elasticloadbalancing:DeregisterTargets",
    ]
    resources = ["*"]
  }
}

resource "aws_iam_role_policy" "blue_green" {
  for_each = local.public_services

  name   = "blue-green-lb-access"
  role   = aws_iam_role.blue_green[each.key].id
  policy = data.aws_iam_policy_document.blue_green_permissions[each.key].json
}
