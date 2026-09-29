#!/bin/sh
# Container entry point. THOR_MODE is set per Task state by the state machine; THOR_INPUT carries
# the execution (inspect) or Map item (apply) input — same convention as Thor.Workflows' WorkflowHost.
# Exit 0 = success, anything else = ecs:runTask.sync reports States.TaskFailed.
set -eu

: "${THOR_MODE:?THOR_MODE is required (inspect|apply)}"
: "${THOR_INPUT:?THOR_INPUT is required}"

case "$THOR_MODE" in
  inspect) exec /app/inspect.sh ;;
  apply) exec /app/apply.sh ;;
  *) echo "unknown THOR_MODE '$THOR_MODE'" >&2; exit 1 ;;
esac
