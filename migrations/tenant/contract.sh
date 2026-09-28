#!/bin/sh
# Contract-phase gate. Decides which of one tenant's pending-contract items may be applied now.
#
#   contract.sh <classified.json> <pending-contract.json> <deployed model sha256>
#
#   classified.json        classify.sh output for the tenant; only .deferred is used
#   pending-contract.json  state/pending-contract.json: {"items": {"<sql>": {"firstSeenModel": "<sha256>",
#                          ...}}} — the model version each item first appeared in (fleet expand runs)
#   deployed model sha256  state/deployed.json's desiredSha256: the model the services run now
#
# Prints {"contract":[{table,sql,firstSeenModel,firstSeenAt}],
#         "waiting":[{table,sql,firstSeenModel,firstSeenAt,reason}]}.
#
# Rollback window: an item may go only once a later model version than the one it first appeared in
# is deployed — the release it would roll back to no longer uses it either. An item first seen in the
# deployed version is "too-new"; one with no record (e.g. a column added by hand) is "unknown-age".
# Both wait. Items keep Atlas's order, so dependent statements stay in sequence.
set -eu

[ $# -eq 3 ] || { echo "usage: contract.sh <classified.json> <pending-contract.json> <deployed sha256>" >&2; exit 2; }
for f in "$1" "$2"; do [ -r "$f" ] || { echo "contract.sh: cannot read $f" >&2; exit 2; }; done
# Without the deployed version every item would look old enough — fail instead.
[ -n "$3" ] || { echo "contract.sh: deployed model sha256 is empty" >&2; exit 2; }

jq --slurpfile pending "$2" --arg deployed "$3" '
  ($pending[0].items // {}) as $seen
  | [.deferred[] | {table, sql, firstSeenModel: ($seen[.sql].firstSeenModel // null),
                    firstSeenAt: ($seen[.sql].firstSeenAt // null)}]
  | { contract: map(select(.firstSeenModel != null and .firstSeenModel != $deployed)),
      waiting:  map(select(.firstSeenModel == null or .firstSeenModel == $deployed)
                    | . + {reason: (if .firstSeenModel == null then "unknown-age" else "too-new" end)}) }' "$1"
