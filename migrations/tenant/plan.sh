#!/bin/sh
# CI side (tenant-migrations.yml "plan" job). Diffs every tenant's live-schema snapshot (written by
# the runner's inspect mode) against the desired state and classifies the result. Needs the Atlas
# CLI and an empty Postgres dev database — which is why it runs in CI, not ECS.
#
#   DEV_URL=<postgres url> plan.sh <desired.hcl> <run dir>
#
# <run dir> is runs/<runId>/ downloaded from the bucket: tenants.json, live/<id>.hcl,
# live/<id>.notnull.txt. Writes into it (for upload back to the bucket):
#   plans/<id>.sql     expand SQL to apply (tenants with changes and no blocking finding)
#   summary.json       per-tenant expand / deferred / blocking
#   manifest.json      [{tenantId, planSha256, liveSha256}] — the Apply Map's items
#   plan-result.json   {changedTenants, blockedTenants}
set -eu

: "${DEV_URL:?DEV_URL is required}"
[ $# -eq 2 ] || { echo "usage: DEV_URL=... plan.sh <desired.hcl> <run dir>" >&2; exit 2; }

here=$(cd "$(dirname "$0")" && pwd)
desired=$(cd "$(dirname "$1")" && pwd)/$(basename "$1")
run=$2
work=$(mktemp -d)
mkdir -p "$run/plans"

# Not piped: an Atlas failure must fail the job, not read as "no changes".
diff_sql() { # live.hcl out [env]
  atlas schema diff -c "file://$here/atlas.hcl" ${3:+--env "$3"} --var dev_url="$DEV_URL" \
    --dev-url "$DEV_URL" --from "file://$1" --to "file://$desired" --format '{{ sql . }}' > "$2"
}

: > "$work/summary.ndjson"
for id in $(jq -r '.[].tenantId' "$run/tenants.json"); do
  echo "planning tenant $id" >&2
  diff_sql "$run/live/$id.hcl" "$work/$id.expand.sql" expand
  diff_sql "$run/live/$id.hcl" "$work/$id.full.sql"
  "$here/classify.sh" "$work/$id.expand.sql" "$work/$id.full.sql" "$run/live/$id.notnull.txt" \
    > "$work/$id.json"

  plan_sha=""
  if [ -s "$work/$id.expand.sql" ]; then
    cp "$work/$id.expand.sql" "$run/plans/$id.sql"
    plan_sha=$(sha256sum "$run/plans/$id.sql" | cut -d' ' -f1)
  fi
  live_sha=$(jq -r --arg id "$id" '.[] | select(.tenantId == $id) | .liveSha256' "$run/tenants.json")

  jq -c --arg id "$id" --arg plan "$plan_sha" --arg live "$live_sha" \
    '{tenantId: $id, planSha256: $plan, liveSha256: $live} + .' "$work/$id.json" >> "$work/summary.ndjson"
done

jq -s '.' "$work/summary.ndjson" > "$run/summary.json" # [] when there are no tenants
jq '[.[] | select(.planSha256 != "" and (.blocking | length) == 0) | {tenantId, planSha256, liveSha256}]' \
  "$run/summary.json" > "$run/manifest.json"
jq '{changedTenants: [.[] | select(.planSha256 != "")] | length,
     blockedTenants: [.[] | select((.blocking | length) > 0)] | length}' \
  "$run/summary.json" > "$run/plan-result.json"
cat "$run/plan-result.json"
