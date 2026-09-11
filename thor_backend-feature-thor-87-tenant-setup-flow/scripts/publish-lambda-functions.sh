#!/usr/bin/env bash
# Publishes every Lambda function under backend/functions/ into its own
# publish/ directory, so Terraform's archive_file data sources have real,
# compiled output to zip — Terraform can't compile C#, only zip existing
# files. Run automatically by infra/root.hcl's before_hook on every
# `terragrunt plan`/`apply`, so a new function under backend/functions/
# gets picked up with no infra.yml or root.hcl changes needed.
#
# Generic by convention: any backend/functions/<Name>/src/<Name>.Function/*.csproj
# (the thin Lambda entry point in a Core/Function split, e.g. Thor.Authorizer.Function)
# is published to backend/functions/<Name>/publish, matching how each lambda module's
# *_source_dir variable is wired in each environment's terragrunt.hcl (get_repo_root()
# + this same path). dotnet publish on the .Function project pulls in its Core project
# reference automatically, so only the entry point needs to be targeted directly.
#
# Skips a function's build if the hash of its src/ (plus global.json, which
# pins the SDK version and can change compiled output) matches the hash
# recorded at the last build — saves a rebuild on repeated local terragrunt
# runs. Doesn't help CI: every CI job starts from a fresh checkout with
# nothing to compare against, so this is a local-only optimization. Deliberately
# excludes test/, README.md, .editorconfig, and the .slnx — dotnet publish
# on the src project never reads any of them, so hashing them in would only
# cause spurious rebuilds. The hash marker lives next to publish/, not
# inside it — putting it inside would get it zipped into the deployed
# Lambda package.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FUNCTIONS_DIR="${REPO_ROOT}/backend/functions"

if command -v sha256sum >/dev/null 2>&1; then
  HASH_CMD=(sha256sum)
elif command -v shasum >/dev/null 2>&1; then
  HASH_CMD=(shasum -a 256)
else
  echo "Need sha256sum or shasum to hash source files — neither found." >&2
  exit 1
fi

compute_build_hash() {
  local src_dir="$1"
  local global_json="$2"

  {
    find "$src_dir" -type f -print0
    [ -f "$global_json" ] && printf '%s\0' "$global_json" || true
  } | sort -z | xargs -0 "${HASH_CMD[@]}" | "${HASH_CMD[@]}" | cut -d' ' -f1
}

if [ ! -d "$FUNCTIONS_DIR" ]; then
  echo "No backend/functions directory — nothing to publish."
  exit 0
fi

for csproj in "$FUNCTIONS_DIR"/*/src/*.Function/*.csproj; do
  [ -e "$csproj" ] || continue

  project_dir="$(dirname "$csproj")"
  src_dir="$(dirname "$project_dir")"
  function_dir="$(dirname "$src_dir")"
  function_name="$(basename "$function_dir")"
  publish_dir="${function_dir}/publish"
  hash_file="${function_dir}/.publish-hash"

  current_hash="$(compute_build_hash "$src_dir" "${function_dir}/global.json")"

  if [ -d "$publish_dir" ] && [ -f "$hash_file" ] && [ "$current_hash" = "$(cat "$hash_file")" ]; then
    echo "Skipping ${function_name} — src/ unchanged since last publish."
    continue
  fi

  echo "Publishing ${function_name}..."
  dotnet publish "$csproj" -c Release -o "$publish_dir"
  echo "$current_hash" > "$hash_file"
done
