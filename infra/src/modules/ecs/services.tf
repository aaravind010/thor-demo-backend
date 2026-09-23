locals {
  name_prefix = { for k, v in var.services : k => "thor-${var.environment}-${k}" }

  # Falls back to that service's own ECR repo at the "latest" tag when container_image is left blank.
  resolved_image = {
    for k, v in local.active_services : k =>
    v.container_image != "" ? v.container_image : "${aws_ecr_repository.thor-ecr-repo[k].repository_url}:latest"
  }

  # thor-api's TLS follows the NLB's own state; internal services are always TLS.
  container_tls_enabled = {
    for k, v in local.active_services : k =>
    v.expose_via_nlb ? local.nlb_tls_enabled : true
  }
}

resource "aws_cloudwatch_log_group" "thor-svc-logs" {
  for_each = local.active_services

  name              = "/ecs/${var.environment}/${each.key}"
  retention_in_days = each.value.log_retention_days
  tags              = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_ecs_task_definition" "thor-svc-taskdef" {
  for_each = local.active_services

  family                   = local.name_prefix[each.key]
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = each.value.cpu
  memory                   = each.value.memory
  execution_role_arn       = aws_iam_role.execution[each.key].arn
  task_role_arn            = aws_iam_role.task[each.key].arn

  container_definitions = jsonencode([
    {
      name      = each.key
      image     = local.resolved_image[each.key]
      essential = true

      portMappings = [
        {
          name          = "app"
          containerPort = each.value.container_port
          protocol      = "tcp"
        }
      ]

      environment = concat(
        [for k, v in each.value.environment_variables : { name = k, value = v }],
        # TLS cert path/password baked into the image by the Dockerfile's openssl step.
        local.container_tls_enabled[each.key] ? (
          each.key == "intelligence-engine" ? [
            { name = "TLS_CERT_PATH", value = "/app/certs/server.crt" },
            { name = "TLS_CERT_KEY_PATH", value = "/app/certs/server.key" },
            ] : [
            { name = "TLS_CERT_PFX_PATH", value = "/app/certs/server.pfx" },
            { name = "TLS_CERT_PFX_PASSWORD", value = "thor-internal" },
          ]
        ) : []
      )

      secrets = [
        for k, v in each.value.secrets : { name = k, valueFrom = v }
      ]

      # Lets ECS detect a hung-but-running container on task-api/intelligence-engine, which have no ALB health check.
      # intelligence-engine is pure gRPC (h2-only) and won't answer a plain HTTP(S) GET, so it's probed via the
      # standard grpc.health.v1.Health service instead (assumes the image has curl on PATH for the other services;
      # -k skips validation of the self-signed cert).
      healthCheck = {
        command = each.key == "intelligence-engine" ? [
          "CMD-SHELL", "python -m thor_intelligence_engine.healthcheck || exit 1"
          ] : (
          local.container_tls_enabled[each.key] ? [
            "CMD-SHELL", "curl -k -f https://localhost:${each.value.container_port}${each.value.health_check_path} || exit 1"
            ] : [
            "CMD-SHELL", "curl -f http://localhost:${each.value.container_port}${each.value.health_check_path} || exit 1"
          ]
        )
        interval    = 30
        timeout     = 5
        retries     = 3
        startPeriod = 10
      }

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.thor-svc-logs[each.key].name
          "awslogs-region"        = data.aws_region.current.region
          "awslogs-stream-prefix" = each.key
        }
      }
    }
  ])

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_ecs_service" "thor-svc" {
  for_each = local.active_services

  name            = each.key
  cluster         = aws_ecs_cluster.thor-ecs-cluster.id
  task_definition = aws_ecs_task_definition.thor-svc-taskdef[each.key].arn
  desired_count   = each.value.desired_count
  launch_type     = "FARGATE"

  deployment_minimum_healthy_percent = each.value.min_healthy_percent
  deployment_maximum_percent         = each.value.max_percent

  # Automatically rolls back to the last known-good revision if new tasks fail health checks.
  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [aws_security_group.service[each.key].id]
    assign_public_ip = false
  }

  dynamic "load_balancer" {
    for_each = each.value.expose_via_nlb ? [1] : []
    content {
      target_group_arn = aws_lb_target_group.thor-nlb-tg-blue[each.key].arn
      container_name   = each.key
      container_port   = each.value.container_port

      dynamic "advanced_configuration" {
        for_each = each.value.deployment_strategy == "BLUE_GREEN" ? [1] : []
        content {
          alternate_target_group_arn = aws_lb_target_group.thor-nlb-tg-green[each.key].arn
          production_listener_rule   = aws_lb_listener.thor-nlb-listener[each.key].arn
          role_arn                   = aws_iam_role.blue_green[each.key].arn
        }
      }
    }
  }

  deployment_controller {
    type = "ECS"
  }

  # Native ECS blue/green, no CodeDeploy — works for task-api/intelligence-engine too via a task-set swap, no ALB required.
  deployment_configuration {
    strategy             = each.value.deployment_strategy
    bake_time_in_minutes = each.value.deployment_strategy == "BLUE_GREEN" ? each.value.bake_time_in_minutes : null
  }

  service_connect_configuration {
    enabled   = true
    namespace = aws_service_discovery_http_namespace.thor-sc-namespace.arn

    service {
      port_name      = "app"
      discovery_name = each.key

      client_alias {
        port     = each.value.container_port
        dns_name = each.key
      }
    }
  }

  # CI/CD updates task_definition/desired_count — Terraform must ignore both.
  # load_balancer temporarily NOT ignored: needed for one apply so a deployment_strategy change (e.g. ROLLING -> BLUE_GREEN) can actually push its required advanced_configuration through. Re-add load_balancer here once this apply succeeds, since blue/green's live primary-target-group swap needs it ignored again afterward.
  lifecycle {
    ignore_changes = [task_definition, desired_count]
  }

  # Depends on all instances of aws_lb_listener.thor-nlb-listener, which is zero for services with no listener — no conditional needed.
  depends_on = [aws_lb_listener.thor-nlb-listener]

  tags = var.tags
}
