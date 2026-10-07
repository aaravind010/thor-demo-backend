# Workflows live in this state alongside the platform. They were briefly split into their own
# Terraform states; that was reverted once the image tag stopped being Terraform's business, because
# the only thing the split bought was stopping an image deploy from applying unrelated platform
# drift — and an image deploy no longer applies anything at all. See modules/workflow's image_tag.
#
# The platform half (ECR repository, security group, shared cluster) is still its own set of modules
# taking a plain list of names. rds_proxy and neptune build their ingress rules from
# module.workflow_network rather than from a workflow, so nothing about a workflow's own resources
# can feed back into the platform.

module "workflow_registry" {
  source = "./modules/workflow_registry"

  workflows   = toset(keys(var.workflows))
  environment = var.environment
  tags        = var.tags
}

module "workflow_network" {
  source = "./modules/workflow_network"

  workflows   = toset(keys(var.workflows))
  environment = var.environment
  vpc_id      = local.vpc_id
  tags        = var.tags
}

module "workflow_cluster" {
  source = "./modules/workflow_cluster"

  environment               = var.environment
  enable_container_insights = var.enable_container_insights
  tags                      = var.tags
}

# The ECR repository is the only resource this refactor moves that has ever been applied — it was
# unconditional in modules/ingestion, so it exists in dev, qa and prod today. Keeping its name means
# this is a state re-address and nothing else: no destroy, no re-create, no lost images.
moved {
  from = module.ingestion.aws_ecr_repository.ecr_ingestion
  to   = module.workflow_registry.aws_ecr_repository.workflow["ingestion"]
}

moved {
  from = module.ingestion.aws_ecr_lifecycle_policy.ecr_ingestion_lifecycle_policy
  to   = module.workflow_registry.aws_ecr_lifecycle_policy.workflow["ingestion"]
}

# --- the workflows themselves -------------------------------------------------------------------

module "workflow" {
  source   = "./modules/workflow"
  for_each = var.workflows

  name    = each.key
  enabled = each.value.enabled

  environment                  = var.environment
  account_id                   = var.account_id
  aws_region                   = var.aws_region
  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  repository_url = module.workflow_registry.repository_urls[each.key]

  cluster_name       = module.workflow_cluster.cluster_name
  security_group_id  = module.workflow_network.security_group_ids[each.key]
  private_subnet_ids = local.private_subnet_ids

  steps                  = local.workflow_definitions[each.key].steps
  buckets                = local.workflow_definitions[each.key].buckets
  environment_variables  = local.workflow_definitions[each.key].environment_variables
  definition_vars        = local.workflow_definitions[each.key].definition_vars
  chained_workflows      = local.workflow_definitions[each.key].chained_workflows
  base_policy_statements = local.workflow_compute_policy_statements
  # Null (no override in the definition) falls back to the module default.
  task_cpu    = try(local.workflow_definitions[each.key].task_cpu, null)
  task_memory = try(local.workflow_definitions[each.key].task_memory, null)

  tags = var.tags
}

# A chain target's ARN is a built string, never a module reference — that is what keeps A -> B and
# B -> A free of a Terraform cycle, and it is also why nothing else notices a target that this
# environment does not host or hosts with enabled = false. The failure would otherwise be a
# StartExecution at runtime against a state machine that does not exist, long after the apply.
#
# A check rather than a validation: var.workflows and local.workflow_definitions are separate inputs
# and only this file sees both, and an environment may legitimately be mid-rollout with the target
# not yet enabled — a warning is the right weight for that, a failed plan is not.
check "chained_workflows_are_hosted_here" {
  assert {
    # Only workflows that are themselves enabled: a disabled one has no state machine and so cannot
    # start anything, which is the normal state of an environment that hosts neither yet.
    condition = alltrue(flatten([
      for name, definition in local.workflow_definitions : [
        for target in definition.chained_workflows :
        try(var.workflows[target].enabled, false)
      ] if try(var.workflows[name].enabled, false)
    ]))
    error_message = "A workflow chains to one that this environment does not host, or hosts with enabled = false. Its StartExecution will fail at runtime. Check chained_workflows in workflow_definitions.tf against var.workflows."
  }
}

# Only for workflows started by an upload. One that is scheduled, or chained off another workflow,
# simply declares no trigger and gets none.
module "workflow_trigger" {
  source   = "./modules/workflow_trigger_s3"
  for_each = { for name, w in var.workflows : name => w if local.workflow_definitions[name].trigger != null }

  name    = each.key
  enabled = each.value.enabled

  environment                  = var.environment
  iam_permissions_boundary_arn = local.iam_permissions_boundary_arn

  # The uploads bucket is an input. module.uploads owns it, it holds live tenant scan data, and no
  # workflow may declare it — modules/ingestion declared one resolving to the same name, which would
  # have failed its first apply with BucketAlreadyOwnedByYou.
  bucket_id     = module.uploads.bucket_name
  bucket_arn    = module.uploads.bucket_arn
  filter_prefix = local.workflow_definitions[each.key].trigger.filter_prefix

  queue_name        = module.workflow[each.key].trigger_queue_name
  dlq_arn           = module.workflow[each.key].dlq_arn
  state_machine_arn = module.workflow[each.key].state_machine_arn

  handler_source_dir        = local.workflow_definitions[each.key].trigger.source_dir
  handler_entrypoint        = local.workflow_definitions[each.key].trigger.entrypoint
  handler_environment       = local.workflow_definitions[each.key].trigger.environment
  handler_policy_statements = local.workflow_definitions[each.key].trigger.policy_statements
  state_machine_env_var     = local.workflow_definitions[each.key].trigger.state_machine_env_var

  private_subnet_ids = local.private_subnet_ids
  security_group_id  = module.workflow_network.security_group_ids[each.key]

  tags = var.tags
}
