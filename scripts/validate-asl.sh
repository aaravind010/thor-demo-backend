#!/usr/bin/env bash
#
# Validates every workflow state machine definition with AWS's own ASL validator.
#
# The definitions in infra/src/modules/workflow/asl/ are templatefile inputs, so they carry
# ${placeholders} where ARNs belong. Those are substituted with well-formed dummies here — the
# validator checks structure, not whether a resource exists — and the result is checked with
# ValidateStateMachineDefinition, which creates nothing.
#
# This is the check that was missing while the definition was generated in HCL: no ASL tooling can
# read Terraform locals, so the first validation was AWS rejecting the apply. It catches
# DUPLICATE_STATE_NAME, unreachable states and unresolved transitions before a plan runs.
#
# Needs only sts:GetCallerIdentity-level credentials; the API is read-only.

set -euo pipefail

ASL_DIR="${1:-infra/src/modules/workflow/asl}"

if [ ! -d "$ASL_DIR" ]; then
  echo "::error::$ASL_DIR does not exist" >&2
  exit 1
fi

shopt -s nullglob
files=("$ASL_DIR"/*.asl.json)
if [ ${#files[@]} -eq 0 ]; then
  echo "::error::no *.asl.json under $ASL_DIR" >&2
  exit 1
fi

failed=0

# Rendered next to the repo rather than in $TMPDIR: on Git Bash a /tmp path is not resolvable by the
# Windows-native AWS CLI, and a relative file:// works on both.
work=".asl-validate"
mkdir -p "$work"
trap 'rm -rf "$work"' EXIT

for f in "${files[@]}"; do
  name=$(basename "$f")
  resolved="$work/$name"

  # Dummy substitution. Shapes matter (an ARN must look like an ARN, subnets must be a JSON array);
  # the values do not.
  #
  # Order matters for map_max_concurrency: it comes from var.definition_vars and sits unquoted in the
  # definition, where JSON wants a number, so it must be replaced with a number before the catch-all
  # below turns every remaining placeholder into the bare word `placeholder`.
  sed -E \
    -e 's/\$\{subnet_ids\}/subnet-aaaaaaaa","subnet-bbbbbbbb/g' \
    -e 's/\$\{lambda_arn_[a-z0-9_]+\}/arn:aws:lambda:us-east-2:111122223333:function:placeholder/g' \
    -e 's/\$\{task_definition_arn_[a-z0-9_]+\}/arn:aws:ecs:us-east-2:111122223333:task-definition\/placeholder:1/g' \
    -e 's/\$\{state_machine_arn_[a-z0-9_]+\}/arn:aws:states:us-east-2:111122223333:stateMachine:placeholder/g' \
    -e 's#\$\{dlq_url\}#https://sqs.us-east-2.amazonaws.com/111122223333/placeholder#g' \
    -e 's/\$\{map_max_concurrency\}/5/g' \
    -e 's/\$\{[a-z0-9_]+\}/placeholder/g' \
    "$f" > "$resolved"

  # Any placeholder left means the substitution list above has drifted from the definitions.
  if grep -q '\${' "$resolved"; then
    echo "::error file=$f::unsubstituted placeholder(s): $(grep -o '\${[a-z0-9_]*}' "$resolved" | sort -u | tr '\n' ' ')" >&2
    failed=1
    continue
  fi

  result=$(aws stepfunctions validate-state-machine-definition \
    --definition "file://$resolved" --type STANDARD --output json)

  if [ "$(echo "$result" | jq -r '.result')" = "OK" ]; then
    echo "  $name: OK"
  else
    echo "::error file=$f::$name failed ASL validation" >&2
    echo "$result" | jq -r '.diagnostics[] | "    \(.severity) \(.code) at \(.location // "?"): \(.message)"' >&2
    failed=1
  fi
done

exit $failed
