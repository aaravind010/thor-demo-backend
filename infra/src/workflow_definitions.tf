# What each workflow is: its steps, its buckets, what its code reads, and how it is triggered.
# Adding ATRE, PAI or Reconcile is an entry here plus a name in each environment's var.workflows —
# no new module, and no state machine written by hand.

locals {
  workflow_rds_db_arn_prefix = "arn:aws:rds-db:${var.aws_region}:${var.account_id}:dbuser:${module.rds_proxy.proxy_resource_id}"

  # Two dbusers: the Master-DB read role and the per-tenant roles (tenant_* covers _rw and _ro).
  # Scoped by proxy resource ID — a cluster-scoped ARN authorizes nothing on this leg.
  workflow_rds_db_connect_resources = [
    "${local.workflow_rds_db_arn_prefix}/${var.master_db_app_user}",
    "${local.workflow_rds_db_arn_prefix}/tenant_*",
  ]

  # Granted to every workflow compute role, ECS task and Lambda alike, so the two targets cannot
  # drift in what the same step code may do. Neptune only when it is on: an empty cluster resource id
  # is not a valid neptune-db ARN.
  workflow_compute_policy_statements = concat(
    [
      {
        actions   = ["s3:GetObject", "s3:ListBucket"]
        resources = [module.uploads.bucket_arn, "${module.uploads.bucket_arn}/*"]
      },
      {
        actions   = ["rds-db:connect"]
        resources = local.workflow_rds_db_connect_resources
      },
      # Broad read on every thor-<environment>-* secret (AD/CyberArk/Windows connector credentials) —
      # the step picks which one it needs at runtime, so it cannot be scoped to a fixed ARN. On the
      # compute role, not an execution role: this is the container's own SDK call.
      {
        actions   = ["secretsmanager:GetSecretValue"]
        resources = ["arn:aws:secretsmanager:${var.aws_region}:${var.account_id}:secret:thor-${var.environment}-*"]
      },
    ],
    var.enable_neptune ? [{
      actions = [
        "neptune-db:StartLoaderJob",
        "neptune-db:GetLoaderJobStatus",
        "neptune-db:CancelLoaderJob",
        "neptune-db:ReadDataViaQuery",
        "neptune-db:WriteDataViaQuery",
        "neptune-db:GetQueryStatus",
      ]
      resources = ["arn:aws:neptune-db:${var.aws_region}:${var.account_id}:${module.neptune[0].cluster_resource_id}/*"]
    }] : [],
  )

  # Read by TenantConnectionManagerFactory. Identical for both compute targets: neither uses password
  # auth, both mint an RDS IAM token for THOR_MASTERDB_USER. THOR_AWS_REGION is explicit because
  # Fargate, unlike Lambda, never sets AWS_REGION.
  workflow_common_environment = {
    THOR_MASTERDB_HOST     = module.rds_proxy.endpoint
    THOR_MASTERDB_DATABASE = module.aurora.database_name
    THOR_MASTERDB_USER     = var.master_db_app_user
    THOR_MASTERDB_REGION   = var.aws_region
    THOR_MASTERDB_PORT     = "5432"
    THOR_MASTERDB_USESSL   = "true"
    THOR_AWS_REGION        = var.aws_region
  }

  workflow_definitions = {
    ingestion = {
      # One entry per THOR_STEP: which compute it needs, and under which role. How these are wired
      # into states — extract-stage running twice, the Choice on $.Compute.Target, the Map fan-out,
      # the poll loop — is in modules/workflow/asl/ingestion.asl.json, which is what Step Functions
      # runs and what you read when debugging an execution.
      #
      # "both" means the definition can send the step to either target at run time. It does not
      # decide; select-compute does, by measuring the manifest.
      steps = {
        "select-compute" = { compute = "lambda" }

        # ecs:runTask.sync has no return-value channel, so the list-mode state that hands back the
        # file list must be the Lambda half. The Map body may be either.
        "extract-stage" = { compute = "both" }

        "promote"          = { compute = "both" }
        "graph-load-start" = { compute = "both", role = "graph-load" }

        # Lambda-only: a poll step branches on its own result, and ecs:runTask.sync cannot
        # distinguish "still loading" from "failed".
        "graph-load-poll" = { compute = "lambda", role = "graph-load" }
      }

      # Neptune bulk-load payloads. Their own bucket, not a prefix in the uploads bucket: that is
      # where they used to go, under an unfiltered notification that fed them straight back into the
      # pipeline. Only the graph-load role can write here, and only Neptune's loader reads it.
      buckets = {
        graphload = {
          retention_days = 14
          write_roles    = ["graph-load"]
          env_var        = "THOR_GRAPH_BULKLOAD_BUCKET"
        }
      }

      environment_variables = merge(local.workflow_common_environment, {
        THOR_NEPTUNE_ENDPOINT                   = var.enable_neptune ? module.neptune[0].endpoint : ""
        THOR_NEPTUNE_PORT                       = "8182"
        THOR_NEPTUNE_ENABLESSL                  = "true"
        THOR_GRAPH_BULKLOAD_IAM_ROLE_ARN        = var.enable_neptune ? module.neptune[0].bulk_load_role_arn : ""
        THOR_GRAPH_BULKLOAD_START_STALE_SECONDS = "900"
        # Read by SizeThresholdPolicy.FromEnvironment() in the select-compute step.
        THOR_COMPUTE_LAMBDA_MAX_BYTES = "5242880"
      })

      trigger = {
        # The only literal prefix that matches every upload: Thor.TaskApi presigns
        # tenants/{tenantId}/uploads/{scanId}/{sourceId}/{file} (Services/UploadService.cs), tenantId
        # varies, and S3 notification filters take no wildcards. It does not match graph-bulk-load/...,
        # which is what the pipeline itself writes — that overlap was the feedback loop.
        filter_prefix = "tenants/"
        source_dir    = var.create_manifest_source_dir
        entrypoint    = "Thor.CreateManifest.Function::Thor.CreateManifest.Function.Function::FunctionHandler"
        # Thor.CreateManifest/CompositionRoot.cs's existing contract. Renaming it belongs with folding
        # the handler into the workflow image, not here.
        state_machine_env_var = "THOR_INGESTION_STATE_MACHINE_ARN"
        environment = {
          THOR_MASTERDB_HOST     = module.rds_proxy.endpoint
          THOR_MASTERDB_DATABASE = module.aurora.database_name
          THOR_MASTERDB_USER     = var.master_db_app_user
          THOR_MASTERDB_REGION   = var.aws_region
          THOR_MASTERDB_PORT     = "5432"
          THOR_MASTERDB_USESSL   = "true"
        }
        policy_statements = [{
          actions   = ["rds-db:connect"]
          resources = local.workflow_rds_db_connect_resources
        }]
      }

      definition_vars = {}

      # Ingestion's last states start ATRE and then Ownership, each returning immediately — see
      # StartAtre and StartOwnership in asl/ingestion.asl.json. Naming them here is what produces the
      # ${state_machine_arn_atre} / ${state_machine_arn_ownership} placeholders those states
      # substitute, and the states:StartExecution grant behind each.
      chained_workflows = ["atre", "ownership"]

      # Fargate memory (MiB) for the ECS half of the "both" steps. Must be valid for task_cpu (1024
      # CPU allows 2048-8192 in 1024 steps).
      task_memory = "4096"
    }

    atre = {
      # All Lambda. Classification is per account and reads no other account's result, so the work
      # parallelizes cleanly: a Distributed Map runs a wave of chunks, each chunk reading its own
      # window of the accounts in scope. See asl/atre.asl.json.
      steps = {
        # Runs once: opens the run's workflow row and hands back the first wave. Must be lambda —
        # the state machine reads that wave off its response.
        "start-run" = { compute = "lambda" }

        # The Map body, one invocation per chunk. Lambda rather than "both" because a Fargate task
        # per chunk would pay a container cold start each time, and ecs:runTask.sync cannot return
        # the row count the next state needs.
        "classify" = { compute = "lambda" }

        # Runs once per wave: reads what that wave did and builds the next, or ends the loop.
        "next-wave" = { compute = "lambda" }

        # Runs once, after the last wave: closes the workflow row.
        "finalize" = { compute = "lambda" }

        # On the Catch path: closes the workflow row as failed. A step cannot record its own timeout
        # or OOM kill, so the state machine records it instead — see RecordFailure in
        # asl/atre.asl.json.
        "record-failure" = { compute = "lambda" }

        # On the Map item's Catch path: writes down the window a failed chunk never classified, so
        # the run can tolerate it and carry on without losing track of what was skipped.
        "record-chunk-failure" = { compute = "lambda" }
      }

      # No workflow-declared buckets, and no Map results bucket either: ATRE's Map hands its item
      # outputs back inline because next-wave has to read them, and a ResultWriter would replace
      # them. modules/workflow creates that bucket only when the definition names it.
      buckets = {}

      environment_variables = merge(local.workflow_common_environment, {
        # Rows one Map item reads, and so how long one invocation runs. Environment configuration
        # because the right number depends on this environment's Aurora capacity; AtreWaves falls
        # back to its own defaults when either is unset.
        THOR_ATRE_ACCOUNTS_PER_CHUNK = try(var.workflows["atre"].settings.accounts_per_chunk, "10000")

        # Chunks handed to one Map. Keep it equal to map_max_concurrency below, or a wave leaves
        # concurrency idle.
        THOR_ATRE_CHUNKS_PER_WAVE = try(var.workflows["atre"].settings.chunks_per_wave, "5")
      })

      # The Map's MaxConcurrency. Each classify invocation opens a read and a write connection, so
      # this bounds concurrent connections through the RDS Proxy at twice its value — a statement
      # about Aurora capacity rather than about ATRE's shape, which is why it is configuration and
      # not a literal in the definition.
      definition_vars = {
        map_max_concurrency = try(var.workflows["atre"].settings.map_max_concurrency, "5")
      }

      # Started by ingestion, not by an upload. Declaring no trigger is what stops
      # modules/workflow_trigger_s3 being instantiated for it at all.
      trigger           = null
      chained_workflows = []
    }

    ownership = {
      # All Lambda, shaped like ATRE: a Distributed Map runs a wave of chunks, each reading its own
      # window of one phase's entities. Three phases — account, grp, asset — run one after another
      # inside the same loop, and each opens with the walk step. See asl/ownership.asl.json.
      steps = {
        # Runs once: validates the request, opens the workflow row, seeds the rule catalog, and hands
        # back the first wave. Must be lambda — the state machine reads that wave off its response.
        "start-run" = { compute = "lambda" }

        # Once per phase, before its first wave: propagates owners down the hierarchies the walk
        # rules follow. Lambda because it answers IsInProgress, which the state machine branches on.
        "walk" = { compute = "lambda" }

        # The Map body, one invocation per chunk. Lambda for ATRE's reasons: no container cold start
        # per chunk, and next-wave has to read the row count it returns.
        "vote" = { compute = "lambda" }

        # Once per wave: reads what the wave did and builds the next, or ends the loop.
        "next-wave" = { compute = "lambda" }

        # After the last phase: projects this run's winners into Neptune as OWNED_BY edges. Under
        # their own role, which alone may write the graph-load bucket.
        "graph-load-start" = { compute = "lambda", role = "graph-load" }
        "graph-load-poll"  = { compute = "lambda", role = "graph-load" }

        # Runs once, after the graph load: closes the workflow row and drops the run's walk staging.
        "finalize" = { compute = "lambda" }

        # The Catch paths, as ATRE's.
        "record-failure"       = { compute = "lambda" }
        "record-chunk-failure" = { compute = "lambda" }
      }

      # The OWNED_BY bulk-load payloads. Ownership's own bucket rather than ingestion's, so each
      # workflow's graph-load role writes only its own; Neptune's loader reads both — see
      # neptune_bulk_load_workflows. No Map results bucket: the Map hands its outputs back inline.
      buckets = {
        graphload = {
          retention_days = 14
          write_roles    = ["graph-load"]
          env_var        = "THOR_GRAPH_BULKLOAD_BUCKET"
        }
      }

      environment_variables = merge(local.workflow_common_environment, {
        THOR_NEPTUNE_ENDPOINT                   = var.enable_neptune ? module.neptune[0].endpoint : ""
        THOR_NEPTUNE_PORT                       = "8182"
        THOR_NEPTUNE_ENABLESSL                  = "true"
        THOR_GRAPH_BULKLOAD_IAM_ROLE_ARN        = var.enable_neptune ? module.neptune[0].bulk_load_role_arn : ""
        THOR_GRAPH_BULKLOAD_START_STALE_SECONDS = "900"

        # Entities one Map item reads, and chunks handed to one Map — ATRE's two dials, for the same
        # reason: the right numbers depend on this environment's Aurora capacity. OwnershipWaves falls
        # back to its own defaults when either is unset. Keep chunks_per_wave equal to
        # map_max_concurrency below, or a wave leaves concurrency idle.
        THOR_OWNERSHIP_ENTITIES_PER_CHUNK = try(var.workflows["ownership"].settings.entities_per_chunk, "10000")
        THOR_OWNERSHIP_CHUNKS_PER_WAVE    = try(var.workflows["ownership"].settings.chunks_per_wave, "5")

        # How long one walk invocation keeps starting new levels: ten minutes of the fifteen-minute
        # Lambda timeout, leaving the rest for the level already running when it expires.
        THOR_OWNERSHIP_WALK_BUDGET_SECONDS = try(var.workflows["ownership"].settings.walk_budget_seconds, "600")
      })

      # The Map's MaxConcurrency — each vote invocation holds a read and a write connection, so this
      # bounds connections through the RDS Proxy at twice its value.
      definition_vars = {
        map_max_concurrency = try(var.workflows["ownership"].settings.map_max_concurrency, "5")
      }

      # Started by ingestion (or a caller with a RunId), not by an upload.
      trigger           = null
      chained_workflows = []
    }
  }
}
