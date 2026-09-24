#!/bin/sh
# MODE=inspect. Snapshots the live tenant schema of every selected tenant for CI to plan against.
# Read-only against every database; no dev database needed.
#
# THOR_INPUT: {"runId": "...", "tenant": "all" | "<tenant_id>", ...}
# Writes  runs/<runId>/live/<tenant_id>.hcl          live schema (HCL)
#         runs/<runId>/live/<tenant_id>.notnull.txt  "table.column" NOT NULL without default
#         runs/<runId>/tenants.json                  [{tenantId, liveSha256}]
# shellcheck source=lib.sh
. /app/lib.sh

run_id=$(input .runId)
tenant=$(input .tenant)
work=$(mktemp -d)

resolve_tenants "$tenant" > "$work/tenants.tsv"
# A named tenant that doesn't resolve fails closed; "all" over an environment with no tenants yet
# is a valid, empty snapshot.
[ -s "$work/tenants.tsv" ] || [ "$tenant" = "all" ] || die "no active tenant matches '$tenant'"

: > "$work/tenants.ndjson"
# fd 3: psql/atlas inside the loop must not read the tenant list from stdin.
while IFS="$TAB" read -r id host db user <&3; do
  log "inspecting tenant $id ($db)"
  token=$(iam_token "$host" "$user")

  atlas_inspect "$host" "$db" "$user" "$token" "$work/$id.hcl"
  tenant_psql "$host" "$db" "$user" "$token" -At -c "
    SELECT table_name || '.' || column_name FROM information_schema.columns
    WHERE table_schema = 'tenant' AND is_nullable = 'NO' AND column_default IS NULL
      AND is_identity = 'NO' AND is_generated = 'NEVER'" > "$work/$id.notnull.txt"

  s3_put "$work/$id.hcl" "runs/$run_id/live/$id.hcl"
  s3_put "$work/$id.notnull.txt" "runs/$run_id/live/$id.notnull.txt"
  jq -cn --arg id "$id" --arg sha "$(sha256 "$work/$id.hcl")" '{tenantId: $id, liveSha256: $sha}' \
    >> "$work/tenants.ndjson"
done 3< "$work/tenants.tsv"

jq -s '.' "$work/tenants.ndjson" > "$work/tenants.json" # [] when there are no tenants
s3_put "$work/tenants.json" "runs/$run_id/tenants.json"
log "inspected $(jq length "$work/tenants.json") tenant(s)"
