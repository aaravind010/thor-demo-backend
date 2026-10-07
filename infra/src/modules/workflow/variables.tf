# The workflow engine's inputs. Everything workflow-specific arrives as data: the step list, the
# extra buckets, the IAM statements. Nothing in this module names "ingestion", and adding ATRE/PAI/
# Reconcile is a new instantiation, not a new module.

variable "name" {
  description = "Workflow name, e.g. \"ingestion\". Used in every resource name and as the ECR repository key."
  type        = string
}

variable "enabled" {
  description = "Create the workflow's resources. False leaves only what the platform owns (registry, security group, cluster), so a new environment can be bootstrapped before any image exists."
  type        = bool
  default     = false
}

# --- the step list -----------------------------------------------------------------------------

variable "steps" {
  description = <<-EOT
    The compute each THOR_STEP needs, keyed by step name. One entry per step, not per state — the
    orchestration (how many states a step expands into, in what order, with what results) lives in
    asl/<name>.asl.json, which is the artifact Step Functions actually runs.

      compute  "lambda" | "ecs" | "both". Decides which halves get built. "both" is for a step the
               definition can send to either target at run time via a Choice on $.Compute.Target;
               it does not itself choose, it only makes both reachable.
      role     Which compute role variant this step runs under. Defaults to "default".

    A step that returns a value to the state machine must be "lambda": ecs:runTask.sync hands back
    the task description, not container output.

    Starting another workflow is not a step and does not belong here — it runs no image and needs no
    compute. See var.chained_workflows.
  EOT

  type = map(object({
    compute = string
    role    = optional(string, "default")
  }))

  validation {
    condition     = length(var.steps) > 0
    error_message = "steps must not be empty."
  }

  validation {
    condition     = alltrue([for n, s in var.steps : contains(["lambda", "ecs", "both"], s.compute)])
    error_message = "steps[*].compute must be one of: lambda, ecs, both."
  }
}

variable "definition_vars" {
  description = <<-EOT
    Extra $${...} substitutions for this workflow's asl/<name>.asl.json, on top of the ARNs and names
    this module already supplies.

    For numbers the definition treats as configuration rather than as part of the graph — a Map's
    MaxConcurrency being the case this exists for, because it is really a statement about how much
    Aurora capacity an environment has, not about how the workflow is shaped. Retry counts, backoff
    rates and wait intervals stay literals in the definition: those are orchestration decisions and
    belong with the graph.

    Values substitute as plain text, so a placeholder used where JSON wants a number must be written
    unquoted in the definition.
  EOT

  type    = map(string)
  default = {}

  # The module's own substitutions are built after this map is merged in, so a collision here would
  # be silently overwritten rather than reported. Cheaper to reject the name.
  validation {
    condition = length(setintersection(
      keys(var.definition_vars),
      ["cluster_name", "security_group_id", "container_name", "dlq_url", "subnet_ids", "map_results_bucket"],
    )) == 0
    error_message = "definition_vars must not redefine a substitution this module owns: cluster_name, security_group_id, container_name, dlq_url, subnet_ids, map_results_bucket."
  }

  validation {
    condition     = alltrue([for name in keys(var.definition_vars) : !startswith(name, "lambda_arn_") && !startswith(name, "task_definition_arn_") && !startswith(name, "state_machine_arn_")])
    error_message = "definition_vars must not use the lambda_arn_*, task_definition_arn_* or state_machine_arn_* prefixes — this module generates those from var.steps and var.chained_workflows."
  }
}

variable "chained_workflows" {
  description = <<-EOT
    Other workflows this one may start, by name. Each one named here gets:

      - a $${state_machine_arn_<name>} placeholder for the definition to use in a
        "arn:aws:states:::states:startExecution" task, and
      - a states:StartExecution grant on exactly that ARN.

    The ARN is built from the naming rule rather than read from the other module's output, the same
    way this module already builds its own (see state_machine.tf) and the DLQ builds the trigger
    queue's. That is what keeps the dependency out of the Terraform graph — and therefore what makes
    A -> B and B -> A expressible without a cycle.

    Declaring the name here does not write the state: the definition in asl/<name>.asl.json does,
    which is the whole arrangement this module uses for every other integration. Terraform's job is
    substitution.

    Naming a workflow that this environment does not host, or hosts with enabled = false, produces a
    grant on an ARN nothing answers — see the check block in workflows.tf, which is where the caller
    has the var.workflows map needed to catch it.
  EOT

  type    = list(string)
  default = []
}

# --- image -------------------------------------------------------------------------------------

variable "image_tag" {
  description = <<-EOT
    The floating tag Terraform points at. Deliberately a constant, not a commit SHA: Terraform owns
    the *shape* of the compute, not which build is running, so deploying an image never requires a
    Terraform apply.

    CI pushes <repo>:<sha> immutably, then moves this tag onto it. From there the two targets
    diverge, because of when each resolves the tag:

      ECS     resolves at task launch, so the next RunTask pulls the new image with no new task
              definition revision. That is what lets the state machine keep pinning a revision-
              qualified ARN — the revision never has to change.
      Lambda  resolves at update time, so CI must call update-function-code. It passes this same
              floating tag, so the stored ImageUri still matches what Terraform configured and no
              drift appears.

    The consequence to be clear about: Terraform state no longer records which build is deployed.
    deploy/<name>/image.json does — the same arrangement the three ECS services already use.
  EOT

  type    = string
  default = "deployed"

  validation {
    condition     = var.image_tag != "" && var.image_tag != "latest"
    error_message = "image_tag must be a stable, non-empty floating tag, and not \"latest\" — \"latest\" is conventionally overwritten by anything and makes the deployed build unattributable."
  }
}

variable "repository_url" {
  description = "ECR repository URL for this workflow, from modules/workflow_registry."
  type        = string
}

# --- platform wiring -------------------------------------------------------------------------

variable "cluster_name" {
  description = "Shared workflow ECS cluster name, from modules/workflow_cluster."
  type        = string
}

variable "security_group_id" {
  description = "This workflow's security group, from modules/workflow_network. Owned there, not here, so the platform can build its RDS Proxy and Neptune ingress rules without depending on this module."
  type        = string
}

variable "private_subnet_ids" {
  description = "Subnets the ECS tasks and Lambda ENIs attach to."
  type        = list(string)
}

variable "environment" {
  description = "Deployment environment (dev, qa, prod)."
  type        = string
}

variable "account_id" {
  description = "AWS account ID."
  type        = string
}

variable "aws_region" {
  description = "AWS region."
  type        = string
}

variable "iam_permissions_boundary_arn" {
  description = "Permissions boundary applied to every role this module creates. Required by SCP."
  type        = string
}

# --- IAM ---------------------------------------------------------------------------------------

variable "base_policy_statements" {
  description = "Statements every compute role gets, ECS task roles and Lambda roles alike, so the two targets cannot drift in what the step code may do. Conditional grants (e.g. Neptune only when it is enabled) are the caller's business — pass a shorter list."
  type = list(object({
    actions   = list(string)
    resources = list(string)
  }))
  default = []
}

variable "role_variants" {
  description = "Extra statements per compute role variant, keyed by the name steps reference in `role`. The variant set itself is derived from the steps and buckets, so an entry here is only needed to add statements beyond the base and the bucket grants. IAM cannot distinguish task definitions on a shared role — no condition key carries the family — so a narrower grant genuinely needs its own variant."
  type = map(list(object({
    actions   = list(string)
    resources = list(string)
  })))
  default = {}
}

# --- buckets -----------------------------------------------------------------------------------

variable "buckets" {
  description = <<-EOT
    Extra per-workflow buckets, keyed by short name; created as
    thor-<environment>-workflow-<name>-<key>-<account_id>.

      retention_days  Expire objects after this many days. Omit to keep them.
      write_roles     Role variants granted s3:PutObject on it. Anything not listed cannot write.
      read_roles      Role variants granted s3:GetObject/ListBucket on it.
      env_var         Injected into every compute target as this environment variable, so the caller
                      can point step code at a bucket this module names.

    The Distributed Map's results bucket is not declared here — it is built in, because any workflow
    with a map step needs one and nothing but the state machine touches it.
  EOT

  type = map(object({
    retention_days = optional(number)
    write_roles    = optional(list(string), [])
    read_roles     = optional(list(string), [])
    env_var        = optional(string)
  }))
  default = {}
}

variable "map_results_retention_days" {
  description = "Expiry for the Distributed Map's per-item result objects."
  type        = number
  default     = 30
}

# --- runtime -----------------------------------------------------------------------------------

variable "environment_variables" {
  description = "Environment shared by every compute target. Bucket names from var.buckets are merged in on top, so the caller need not predict names this module owns."
  type        = map(string)
  default     = {}
}

variable "task_cpu" {
  description = "Fargate task CPU units."
  type        = string
  default     = "1024"
  nullable    = false
}

variable "task_memory" {
  description = "Fargate task memory (MiB)."
  type        = string
  default     = "2048"
  nullable    = false
}

variable "lambda_timeout" {
  description = "Per-step Lambda timeout in seconds."
  type        = number
  default     = 900
}

variable "lambda_memory_size" {
  description = "Per-step Lambda memory (MiB)."
  type        = number
  default     = 1024
}

variable "log_retention_days" {
  description = "CloudWatch log retention for the state machine, tasks and step functions."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Tags applied to every resource in this module."
  type        = map(string)
  default     = {}
}
