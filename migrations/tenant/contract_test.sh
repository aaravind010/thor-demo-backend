#!/bin/sh
# Tests for contract.sh — one case per rule. Run: sh migrations/tenant/contract_test.sh
# (CI runs it in tenant-migrations.yml's generate job.)
#
# Each case: the tenant's deferred items ("<table> <sql>" lines), the pending-contract record
# ("<first-seen model> <sql>" lines), the deployed model, then the expected
# "contract <sql>" / "waiting <reason> <sql>" lines.
set -eu

here=$(cd "$(dirname "$0")" && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
failed=0

check() { # name deferred record deployed expected
  printf '%s\n' "$2" | jq -Rn '{deferred: [inputs | select(. != "")
    | {rule: "contract", table: (split(" ")[0]), sql: (split(" ")[1:] | join(" "))}]}' > "$tmp/classified.json"
  printf '%s\n' "$3" | jq -Rn '{items: ([inputs | select(. != "")
    | {key: (split(" ")[1:] | join(" ")), value: {firstSeenModel: split(" ")[0]}}] | from_entries)}' \
    > "$tmp/pending.json"
  actual=$(sh "$here/contract.sh" "$tmp/classified.json" "$tmp/pending.json" "$4" | jq -r '
    (.contract[] | "contract \(.sql)"),
    (.waiting[]  | "waiting \(.reason) \(.sql)")')
  if [ "$actual" = "$5" ]; then
    echo "ok   $1"
  else
    echo "FAIL $1"
    printf '  expected:\n%s\n  actual:\n%s\n' "$5" "$actual"
    failed=1
  fi
}

drop_old='ALTER TABLE "tenant"."scan" DROP COLUMN "old_name"'
drop_tmp='ALTER TABLE "tenant"."scan" DROP COLUMN "tmp"'
tighten='ALTER TABLE "tenant"."scan" ALTER COLUMN "new_name" SET NOT NULL'

check nothing-pending '' '' M3 ''

# Removed in M2, M3 is deployed: the release it would roll back to (M2) no longer uses it.
check older-model "scan $drop_old" "M2 $drop_old" M3 \
"contract $drop_old"

# Removed in the deployed version: rolling back one release would still need it.
check same-model "scan $drop_old" "M3 $drop_old" M3 \
"waiting too-new $drop_old"

# No record of when it appeared (e.g. a column added by hand): wait.
check unknown-age "scan $drop_tmp" "M2 $drop_old" M3 \
"waiting unknown-age $drop_tmp"

# Old and new items in one tenant: only the old ones go, in Atlas's order.
check mixed "scan $drop_old
scan $tighten
scan $drop_tmp" "M2 $drop_old
M3 $tighten" M3 \
"contract $drop_old
waiting too-new $tighten
waiting unknown-age $drop_tmp"

# Fail closed: without the deployed version every item would look old enough.
printf '{"deferred": []}' > "$tmp/c.json"; printf '{"items": {}}' > "$tmp/p.json"
if sh "$here/contract.sh" "$tmp/c.json" "$tmp/p.json" "" 2>/dev/null; then
  echo "FAIL no-deployed-model (should refuse)"; failed=1
else
  echo "ok   no-deployed-model"
fi

exit "$failed"
