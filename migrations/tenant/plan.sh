#!/bin/sh
# CI side (tenant-migrations.yml "plan" job). Builds one plan per tenant for a phase from the runner's
# inspect output. Needs the Atlas CLI and an empty Postgres dev database — which is why it runs in CI,
# not ECS.
#
#   DEV_URL=<postgres url> plan.sh <expand|migrate|contract> <desired.hcl> <run dir>
#
# <run dir> is runs/<runId>/ downloaded from the bucket: tenants.json and live/<id>.{hcl,notnull.txt,
# columns.txt,backfills.tsv}, plus the bucket's state/ objects the phase reads, under state/:
# pending-contract.json (every phase — its items are the known drops classify.sh needs; absent before
# the first fleet run) and deployed.json (contract). Writes into it (for upload back to the bucket):
#   plans/<id>.sql          SQL to apply (tenants with changes)
#   summary.json            per tenant: blocking, plus the phase's own lists
#   manifest.json           [{tenantId, planSha256, liveSha256}] — the Apply Map's items
#   plan-result.json        {changedTenants, blockedTenants}
#   pending-contract.json   expand: every pending-contract item and the model version it first
#                           appeared in; saved to state/ by fleet runs, read by contract.sh
#
# Every phase shares the apply path — each plan runs in one transaction per tenant:
#   expand    classify.sh's expand list
#   migrate   each pending backfill script, then its record row and a schema check
#             (schema_fingerprint.sql). The tenant must be fully expanded: the scripts write the new
#             columns.
#   contract  the pending-contract items old enough to go (contract.sh). The tenant must be fully
#             expanded and backfilled, and the desired state must be the deployed model.
set -eu

: "${DEV_URL:?DEV_URL is required}"
usage() { echo "usage: DEV_URL=... plan.sh <expand|migrate|contract> <desired.hcl> <run dir>" >&2; exit 2; }
[ $# -eq 3 ] || usage
phase=$1
case $phase in expand|migrate|contract) ;; *) usage ;; esac

here=$(cd "$(dirname "$0")" && pwd)
desired=$(cd "$(dirname "$2")" && pwd)/$(basename "$2")
run=$3
work=$(mktemp -d)
mkdir -p "$run/plans"
TAB=$(printf '\t')
desired_sha=$(sha256sum "$desired" | cut -d' ' -f1)

# Before the first fleet run there is no record: every pending item starts its wait now.
pending_record="$run/state/pending-contract.json"
[ -r "$pending_record" ] || { pending_record="$work/no-record.json"; echo '{"items": {}}' > "$pending_record"; }
jq -r '.items | keys[]' "$pending_record" > "$work/known-drops.txt"

if [ "$phase" = contract ]; then
  # Contract drops what the running services no longer use, so it must plan against exactly the
  # model they run — never a branch that hasn't been deployed.
  [ -r "$run/state/deployed.json" ] || { echo "plan.sh: no state/deployed.json — contract needs a deploy first" >&2; exit 1; }
  deployed=$(jq -r '.desiredSha256 // empty' "$run/state/deployed.json")
  [ "$deployed" = "$desired_sha" ] || {
    echo "plan.sh: contract plans only against the deployed model ($deployed), not $desired_sha" >&2; exit 1; }
fi

# Not piped: an Atlas failure must fail the job, not read as "no changes".
diff_sql() { # live.hcl out [env]
  atlas schema diff -c "file://$here/atlas.hcl" ${3:+--env "$3"} --var dev_url="$DEV_URL" \
    --dev-url "$DEV_URL" --from "file://$1" --to "file://$desired" --format '{{ sql . }}' > "$2"
}

# The tenant still has expand work or blocking findings: its new columns may not exist yet.
not_expanded() { # classified.json
  jq -c '[if (.expand | length) + (.blocking | length) > 0 then
            {rule: "not-expanded", detail: "\(.expand | length) expand change(s) and \(.blocking | length) blocking finding(s) not applied — run expand first"}
          else empty end]' "$1"
}

# Only the scripts' own statements are reported (apply.sh turns QUIET off): the setup, record row and
# schema check run quietly, and "-- backfill <id>" before each script tells the run summary which
# script changed how many rows.
migrate_plan() { # backfills.json
  cat <<'SQL'
-- Migrate plan (plan.sh): each backfill script, its record row, then a check that it changed no schema.
\set QUIET on
CREATE TABLE IF NOT EXISTS tenant.__thor_backfills (
  id text PRIMARY KEY, sha256 text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now());
SQL
  cat "$here/schema_fingerprint.sql"
  printf '%s\n' "SELECT pg_temp.thor_schema_fp() AS fp_before \\gset"
  jq -r '.pending[] | "\(.id)\t\(.sha256)"' "$1" | while IFS="$TAB" read -r bid bsha; do
    printf '\n\\echo -- backfill %s\n\\set QUIET off\n' "$bid"
    cat "$here/backfills/$bid.sql"
    # backfills.sh only passes NNNN_<name> ids and hex hashes, so both are safe to quote.
    printf "\n\\\\set QUIET on\nINSERT INTO tenant.__thor_backfills (id, sha256) VALUES ('%s', '%s');\n" "$bid" "$bsha"
    printf '%s\n' "SELECT pg_temp.thor_assert_schema(:'fp_before') \\gset"
  done
}

: > "$work/summary.ndjson"
for id in $(jq -r '.[].tenantId' "$run/tenants.json"); do
  echo "planning tenant $id ($phase)" >&2
  diff_sql "$run/live/$id.hcl" "$work/$id.expand.sql" expand
  diff_sql "$run/live/$id.hcl" "$work/$id.full.sql"
  "$here/classify.sh" "$work/$id.expand.sql" "$work/$id.full.sql" "$run/live/$id.notnull.txt" \
    "$work/known-drops.txt" > "$work/$id.json"

  case $phase in
    expand)
      # The expand list, not expand.sql as is: it also holds the DROP NOT NULLs the policy can't emit.
      jq -r '.expand[] | .sql + ";"' "$work/$id.json" > "$work/$id.plan.sql"
      cp "$work/$id.json" "$work/$id.summary.json"
      ;;
    migrate)
      "$here/backfills.sh" "$here/backfills" "$run/live/$id.backfills.tsv" "$run/live/$id.columns.txt" \
        > "$work/$id.backfills.json"
      : > "$work/$id.plan.sql"
      [ "$(jq '.pending | length' "$work/$id.backfills.json")" -eq 0 ] \
        || migrate_plan "$work/$id.backfills.json" > "$work/$id.plan.sql"
      jq --argjson ready "$(not_expanded "$work/$id.json")" \
        '{backfills: {pending: [.pending[].id], done, skipped}, blocking: ($ready + .blocking)}' \
        "$work/$id.backfills.json" > "$work/$id.summary.json"
      ;;
    contract)
      "$here/backfills.sh" "$here/backfills" "$run/live/$id.backfills.tsv" "$run/live/$id.columns.txt" \
        > "$work/$id.backfills.json"
      "$here/contract.sh" "$work/$id.json" "$pending_record" "$deployed" > "$work/$id.contract.json"
      jq -r '.contract[] | .sql + ";"' "$work/$id.contract.json" > "$work/$id.plan.sql"
      # Dropping a column before its data was copied would lose it.
      backfill_pending=$(jq -c '[([.pending[].id] + [.blocking[].id] | unique) as $ids
        | if ($ids | length) > 0 then {rule: "backfill-pending", detail: ($ids | join(", "))} else empty end]' \
        "$work/$id.backfills.json")
      jq --argjson ready "$(not_expanded "$work/$id.json")" --argjson bf "$backfill_pending" \
        '{contract, waiting, blocking: ($ready + $bf)}' "$work/$id.contract.json" > "$work/$id.summary.json"
      ;;
  esac

  plan_sha=""
  if [ -s "$work/$id.plan.sql" ]; then
    cp "$work/$id.plan.sql" "$run/plans/$id.sql"
    plan_sha=$(sha256sum "$run/plans/$id.sql" | cut -d' ' -f1)
  fi
  live_sha=$(jq -r --arg id "$id" '.[] | select(.tenantId == $id) | .liveSha256' "$run/tenants.json")

  jq -c --arg id "$id" --arg plan "$plan_sha" --arg live "$live_sha" \
    '{tenantId: $id, planSha256: $plan, liveSha256: $live} + .' "$work/$id.summary.json" >> "$work/summary.ndjson"
done

jq -s '.' "$work/summary.ndjson" > "$run/summary.json" # [] when there are no tenants
jq '[.[] | select(.planSha256 != "" and (.blocking | length) == 0) | {tenantId, planSha256, liveSha256}]' \
  "$run/summary.json" > "$run/manifest.json"
jq '{changedTenants: [.[] | select(.planSha256 != "")] | length,
     blockedTenants: [.[] | select((.blocking | length) > 0)] | length}' \
  "$run/summary.json" > "$run/plan-result.json"

# Option A's memory: when each pending-contract item first appeared. Items already on record keep
# their model version, new ones get this one, items no longer pending anywhere drop off.
if [ "$phase" = expand ]; then
  for id in $(jq -r '.[].tenantId' "$run/tenants.json"); do cat "$work/$id.json"; done \
    | jq -n --slurpfile prev "$pending_record" --arg model "$desired_sha" \
        --arg now "$(date -u +%Y-%m-%dT%H:%M:%SZ)" '
        ($prev[0].items // {}) as $seen
        | {items: ([inputs | .deferred[].sql] | unique
                   | map({key: ., value: ($seen[.] // {firstSeenModel: $model, firstSeenAt: $now})})
                   | from_entries)}' > "$run/pending-contract.json"
fi
cat "$run/plan-result.json"
