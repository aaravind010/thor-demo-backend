# CI only (tenant-migrations.yml). Both environments need a throwaway, empty Postgres 16 dev
# database — the GitHub runner's service container. ECS never diffs; it only inspects.
#
# env "ef": the EF model → the desired tenant schema.
#   dotnet ef dbcontext script --project ../../backend/shared/Thor.DataLayer \
#     --startup-project ../../backend/shared/Thor.DataLayer --context TenantDbContext --output ef.sql
#   atlas schema inspect --env ef --url env://src --var dev_url=<url> --format '{{ hcl . }}' > desired.hcl
#   ef.sql is the whole model as DDL (entities, attributes, OnModelCreating's fluent config),
#   produced through TenantDbContextDesignTimeFactory, which never opens a connection.
#
# env "expand": plan.sh's expand-phase diff policy — emits additive changes only (CREATE TABLE,
#   ADD COLUMN, CREATE INDEX, ADD FOREIGN KEY) and skips everything that belongs to the contract
#   phase. classify.sh re-checks the result, so a change this policy lets through is still caught.

variable "dev_url" {
  type = string
}

env "ef" {
  src     = "file://ef.sql"
  dev     = var.dev_url
  schemas = ["tenant"]
}

env "expand" {
  dev = var.dev_url

  diff {
    skip {
      drop_schema        = true
      modify_schema      = true
      drop_table         = true
      rename_table       = true
      drop_column        = true
      modify_column      = true
      drop_index         = true
      modify_index       = true
      drop_foreign_key   = true
      modify_foreign_key = true
    }
  }
}
