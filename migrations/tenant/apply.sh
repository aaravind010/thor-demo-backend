#!/bin/sh
# MODE=apply. Applies one tenant's approved expand plan, in a single transaction.
#
# THOR_INPUT (one Apply Map item):
#   {"runId": "...", "tenantId": "...", "planSha256": "...", "liveSha256": "..."}
# Never applies SQL that wasn't approved: the plan object must hash to the approved plan, and the
# live schema must still hash to the snapshot CI planned against. A failure rolls the whole tenant
# back to its previous (still compatible) schema.
# shellcheck source=lib.sh
. /app/lib.sh

run_id=$(input .runId)
tenant_id=$(input .tenantId)
plan_sha=$(input .planSha256)
live_sha=$(input .liveSha256)
work=$(mktemp -d)

# Re-resolve routing from the master DB rather than trusting where the plan said the tenant lives.
resolve_tenants "$tenant_id" > "$work/tenant.tsv"
[ "$(wc -l < "$work/tenant.tsv")" -eq 1 ] || die "tenant $tenant_id is not an active tenant"
IFS="$TAB" read -r id host db user < "$work/tenant.tsv"

s3_get "runs/$run_id/plans/$id.sql" "$work/plan.sql"
[ "$(sha256 "$work/plan.sql")" = "$plan_sha" ] || die "plan object for $id does not match the approved hash"

token=$(iam_token "$host" "$user")
atlas_inspect "$host" "$db" "$user" "$token" "$work/live.hcl"
[ "$(sha256 "$work/live.hcl")" = "$live_sha" ] || die "tenant $id changed since it was planned — re-run to re-plan"

# The plan's names are schema-qualified, but pin search_path anyway so nothing can land in public.
# SET LOCAL, not a connection option: RDS Proxy pins sessions that set startup options.
{
  echo "SET LOCAL search_path = tenant;"
  echo "SET LOCAL lock_timeout = '5s';"
  echo "SET LOCAL statement_timeout = '15min';"
  cat "$work/plan.sql"
} > "$work/apply.sql"

log "applying plan $plan_sha to tenant $id ($db)"
tenant_psql "$host" "$db" "$user" "$token" --single-transaction -f "$work/apply.sql"

jq -n --arg runId "$run_id" --arg tenantId "$id" --arg sha "$plan_sha" \
  --arg at "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  '{runId: $runId, tenantId: $tenantId, planSha256: $sha, status: "applied", at: $at}' > "$work/result.json"
s3_put "$work/result.json" "runs/$run_id/results/$id.json"
s3_put "$work/result.json" "state/tenants/$id.json"
log "tenant $id applied"
