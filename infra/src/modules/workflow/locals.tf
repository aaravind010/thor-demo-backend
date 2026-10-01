locals {
  active      = var.enabled
  name_prefix = "thor-${var.environment}-workflow-${var.name}"
  image       = "${var.repository_url}:${var.image_tag}"

  # Named here, created in modules/workflow_trigger_s3. Both sides build the name from the same rule
  # rather than passing a resource attribute, which is what keeps the dependency one-way: the trigger
  # reads this module's DLQ ARN, and nothing here reads the trigger.
  trigger_queue_name = "${local.name_prefix}-sqs"

  # --- the state machine definition --------------------------------------------------------------

  # Authored as Amazon States Language in asl/<name>.asl.json, not generated here. This module used
  # to build the whole definition out of HCL, which meant nothing could check it: no ASL tooling
  # reads Terraform locals, and the first validation was AWS rejecting the apply. A committed
  # .asl.json is diffable, opens in Workflow Studio, and is checked in CI by
  # `aws stepfunctions validate-state-machine-definition` (scripts/validate-asl.sh).
  #
  # Terraform's remaining job is substitution — it owns the ARNs, the definition owns everything else.
  asl_path     = "${path.module}/asl/${var.name}.asl.json"
  asl_template = file(local.asl_path)

  # Both derived from the definition rather than declared separately, so neither can disagree with
  # what Step Functions actually runs.
  #
  # Two questions, not one. A Distributed Map runs each item as a child execution of this same state
  # machine, so it needs to start and inspect those whether or not it writes anything to S3. The
  # results bucket is a separate choice: a Map whose item outputs are small enough to come back
  # inline wants no bucket, and a caller that reads those outputs in a later state cannot use a
  # ResultWriter at all, because it replaces the inline result. Deriving both from
  # "map_results_bucket appears in the definition" gave a Map without a ResultWriter no permission to
  # start its own children, and gave every workflow a bucket whether it had a Map or not.
  has_distributed_map = strcontains(local.asl_template, "DISTRIBUTED")
  has_map_results     = strcontains(local.asl_template, "map_results_bucket")

  # Same rule again, for the same reason: a workflow gets a dead-letter queue when its definition
  # says it wants one, not because it exists. A workflow started by another workflow rather than by a
  # queue has nothing to redrive from and a workflow row of its own to record a failure in, so a DLQ
  # for it is an empty queue nobody writes to — which is what ATRE had.
  #
  # Deriving it here rather than from a variable also keeps the cross-check: dlq_url is only supplied
  # to templatefile when this is true, so a definition that sends to a queue it was not given fails
  # the plan instead of substituting an empty string and failing at runtime.
  has_dlq = strcontains(local.asl_template, "dlq_url")

  # Built by hand, exactly as this module builds its own state machine ARN, so a chain never becomes
  # a Terraform dependency edge. Read by asl_vars below and by the StartExecution grant.
  chained_state_machine_arns = {
    for n in var.chained_workflows :
    n => "arn:aws:states:${var.aws_region}:${var.account_id}:stateMachine:thor-${var.environment}-workflow-${n}-sf"
  }

  # templatefile fails on a placeholder the caller did not supply, which is what makes this a real
  # cross-check: a definition naming a step that has no compute here will not plan.
  asl_vars = merge(
    # First, so the module's own substitutions below win on a collision. var.definition_vars
    # validates against that ever happening, which is what makes the order safe rather than subtle.
    var.definition_vars,
    {
      cluster_name      = var.cluster_name
      security_group_id = var.security_group_id
      container_name    = "${local.name_prefix}-container"
      # Joined with an escaped-quote separator, not jsonencode: the definition writes
      # ["$${subnet_ids}"], so the placeholder sits inside the array literal and the file stays
      # parseable JSON. Substitution still produces a plain literal array.
      subnet_ids = join("\",\"", var.private_subnet_ids)
    },
    local.has_dlq ? {
      dlq_url = local.active ? aws_sqs_queue.dlq[0].id : ""
    } : {},
    local.has_map_results ? {
      map_results_bucket = local.active ? aws_s3_bucket.map_results[0].bucket : ""
    } : {},
    # THOR_STEP values are kebab-case; template variable names cannot be.
    { for n in local.lambda_step_names :
      "lambda_arn_${replace(n, "-", "_")}" => local.active ? aws_lambda_function.step[n].arn : ""
    },
    { for n in local.ecs_step_names :
      "task_definition_arn_${replace(n, "-", "_")}" => local.active ? aws_ecs_task_definition.step[n].arn : ""
    },
    # Unlike the two above, these are literal strings rather than resource attributes, so they are
    # the same whether or not this workflow is enabled.
    { for n, arn in local.chained_state_machine_arns :
      "state_machine_arn_${replace(n, "-", "_")}" => arn
    },
  )

  # --- compute derivations -----------------------------------------------------------------------

  # Which THOR_STEP values need each half. A step declared "lambda" gets no task definition and one
  # declared "ecs" gets no function — the old module built both for every step, so select-compute had
  # an ECS task definition that could never be invoked (it returns a value, and ecs:runTask.sync has
  # no channel for one).
  lambda_step_names = [for n, s in var.steps : n if contains(["lambda", "both"], s.compute)]
  ecs_step_names    = [for n, s in var.steps : n if contains(["ecs", "both"], s.compute)]

  step_role = { for n, s in var.steps : n => s.role }

  # Every variant that must exist: whatever the steps ask for, whatever a bucket grants to, whatever
  # the caller added statements for, and always "default".
  role_variant_names = distinct(concat(
    ["default"],
    [for n, s in var.steps : s.role],
    flatten([for key, b in var.buckets : concat(b.write_roles, b.read_roles)]),
    keys(var.role_variants),
  ))
}
