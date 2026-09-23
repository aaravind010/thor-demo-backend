#!/usr/bin/env bash
# Publishes each backend/functions/<Name>'s and backend/workflows/<Name>'s Lambda entry point into its own
# publish/ dir — Terraform can only zip files, not compile C#. Run by root.hcl's before_hook on every terragrunt
# plan/apply/destroy. The entry point is whichever *.csproj under <Name>/src/ (any depth) has
# <AWSProjectType>Lambda</AWSProjectType>; a plain library like Core has no such marker and is skipped — its
# ProjectReference gets pulled in automatically anyway. Workflow modules that ship as a container image
# (no src/, a Dockerfile) never match and are left to the image pipeline.
# Skips a rebuild if src/ + backend/shared/ + global.json are unchanged (local .publish-hash), else falls through
# to a per-function JFrog cache (JFROG_LAMBDA_ARTIFACTS_REPOSITORY, keyed by content hash) before a real dotnet
# publish. Hash excludes bin/obj and lives outside publish/ so it isn't zipped into the deployed package.
#
# TenantDbContext's migration is regenerated from scratch here, every run, rather than committed — org policy
# prohibits committing generated/tool-output code. This is safe only because every tenant database is brand
# new and empty when this migration is applied (see CreateTenantTablesStep) — there's never a prior schema or
# history-table entry to conflict with.
#
# MasterDbContext's Migrations/Master/ is NOT regenerated here — it stays committed, deliberately, as a
# carved-out exception to that policy. Master DB is one long-lived, already-populated database per
# environment; Thor.DbBootstrap.Function.MigrateAsync()s against it directly. Regenerating "from scratch" the
# way Tenant does would mint a migration with a new ID unrelated to what's in that live database's
# __EFMigrationsHistory table, so MigrateAsync would try to re-run CREATE TABLE against tables that already
# exist — breaking the bootstrap Lambda against any already-provisioned environment. See
# backend/shared/Thor.DataLayer/README.md.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCAN_DIRS=("${REPO_ROOT}/backend/functions" "${REPO_ROOT}/backend/workflows")
SHARED_DIR="${REPO_ROOT}/backend/shared"
DATALAYER_DIR="${SHARED_DIR}/Thor.DataLayer"

echo "Regenerating TenantDbContext migration (Migrations/Tenant/ is git-ignored)..."
rm -rf "${DATALAYER_DIR}/Migrations/Tenant"
(
  cd "$REPO_ROOT"
  dotnet tool restore
  # dotnet ef doesn't implicitly restore NuGet packages for the target project before its design-time build.
  dotnet restore "$DATALAYER_DIR"
  dotnet ef migrations add InitialTenantSchema \
    --project "$DATALAYER_DIR" --context TenantDbContext --output-dir Migrations/Tenant
)
echo "  regenerated Migrations/Tenant/."

if command -v sha256sum >/dev/null 2>&1; then
  HASH_CMD=(sha256sum)
elif command -v shasum >/dev/null 2>&1; then
  HASH_CMD=(shasum -a 256)
else
  echo "Need sha256sum or shasum to hash source files — neither found." >&2
  exit 1
fi

# Every function's Lambda entry point pulls in backend/shared/* by ProjectReference, so those sources land in
# the published zip too and must move the hash — a shared-only change (a new Master migration, say) would
# otherwise hit the local skip and the JFrog cache and ship a stale assembly. Deliberately coarse: the whole
# shared tree, not each function's own reference closure, so a Thor.Auth edit rebuilds functions that don't
# reference it. Migrations/Tenant/ is excluded because it's regenerated above with a fresh timestamped id on
# every run — hashing it would miss the cache every time. The model it's generated from is hashed instead.
compute_build_hash() {
  local src_dir="$1"
  local global_json="$2"

  {
    find "$src_dir" -type f -not -path '*/bin/*' -not -path '*/obj/*' -print0
    find "$SHARED_DIR" -type f \
      -not -path '*/bin/*' -not -path '*/obj/*' \
      -not -path '*/test/*' \
      -not -path '*/Migrations/Tenant/*' -print0
    [ -f "$global_json" ] && printf '%s\0' "$global_json" || true
  } | sort -z | xargs -0 "${HASH_CMD[@]}" | "${HASH_CMD[@]}" | cut -d' ' -f1
}

JFROG_CACHE_ENABLED=false
if [ "${GITHUB_ACTIONS:-}" = "true" ] && [ -n "${JFROG_HOSTNAME:-}" ] && [ -n "${JFROG_LAMBDA_ARTIFACTS_REPOSITORY:-}" ] && [ -n "${JFROG_ACCESS_TOKEN:-}" ]; then
  JFROG_CACHE_ENABLED=true
fi

jfrog_cache_url() {
  local function_name="$1"
  local hash="$2"
  echo "https://${JFROG_HOSTNAME}/artifactory/${JFROG_LAMBDA_ARTIFACTS_REPOSITORY}/${function_name}/${hash}.tar.gz"
}

# Restores publish_dir from JFrog on a real cache hit (exit 0); leaves publish_dir untouched on a miss (exit 1).
try_restore_from_jfrog() {
  local function_name="$1"
  local hash="$2"
  local publish_dir="$3"
  local url tmp_tar
  url="$(jfrog_cache_url "$function_name" "$hash")"
  tmp_tar="$(mktemp)"

  if curl -sf -H "Authorization: Bearer ${JFROG_ACCESS_TOKEN}" -o "$tmp_tar" "$url"; then
    rm -rf "$publish_dir"
    mkdir -p "$publish_dir"
    if tar -xzf "$tmp_tar" -C "$publish_dir" 2>/dev/null; then
      rm -f "$tmp_tar"
      return 0
    fi
    echo "  warning: downloaded JFrog cache archive for ${function_name} is corrupt — treating as a cache miss." >&2
    rm -rf "$publish_dir"
  fi

  rm -f "$tmp_tar"
  return 1
}

# Best-effort — a failed upload just means the next run rebuilds this function too, not a broken deploy.
upload_to_jfrog() {
  local function_name="$1"
  local hash="$2"
  local publish_dir="$3"
  local url tmp_tar
  url="$(jfrog_cache_url "$function_name" "$hash")"
  tmp_tar="$(mktemp)"

  if tar -czf "$tmp_tar" -C "$publish_dir" . && curl -sf -X PUT -H "Authorization: Bearer ${JFROG_ACCESS_TOKEN}" -T "$tmp_tar" "$url" >/dev/null; then
    echo "  cached ${function_name} build (${hash:0:12}) to JFrog."
  else
    echo "  warning: failed to upload ${function_name}'s build cache to JFrog — continuing anyway." >&2
  fi
  rm -f "$tmp_tar"
}

function_count=0

for scan_dir in "${SCAN_DIRS[@]}"; do
  [ -d "$scan_dir" ] || continue

  echo "Scanning ${scan_dir} for lambda functions..."

  while IFS= read -r -d '' csproj; do
    grep -q '<AWSProjectType>Lambda</AWSProjectType>' "$csproj" || continue

    function_count=$((function_count + 1))

    # First path segment after $scan_dir is the function's own folder, regardless of how deep the matching
    # csproj sits under its src/ (flat, like src/*.csproj, or layered, like src/*.Function/*.csproj).
    rel_path="${csproj#"$scan_dir"/}"
    function_name="${rel_path%%/*}"
    function_dir="${scan_dir}/${function_name}"
    src_dir="${function_dir}/src"
    publish_dir="${function_dir}/publish"
    hash_file="${function_dir}/.publish-hash"

    echo "Checking ${function_name} (${csproj})..."

    current_hash="$(compute_build_hash "$src_dir" "${function_dir}/global.json")"

    if [ -d "$publish_dir" ] && [ -f "$hash_file" ] && [ "$current_hash" = "$(cat "$hash_file")" ]; then
      echo "  skipping ${function_name} — src/ unchanged since last publish (local check)."
      continue
    fi

    if [ "$JFROG_CACHE_ENABLED" = "true" ]; then
      echo "  checking JFrog cache for ${function_name} (${current_hash:0:12})..."
      if try_restore_from_jfrog "$function_name" "$current_hash" "$publish_dir"; then
        echo "$current_hash" > "$hash_file"
        echo "  restored ${function_name} from JFrog cache — skipped dotnet publish."
        continue
      fi
      echo "  no JFrog cache hit for ${function_name} — publishing fresh."
    fi

    echo "  publishing ${function_name} -> ${publish_dir}"
    dotnet publish "$csproj" -c Release -r linux-x64 --self-contained false -o "$publish_dir"
    echo "$current_hash" > "$hash_file"

    if [ "$JFROG_CACHE_ENABLED" = "true" ]; then
      upload_to_jfrog "$function_name" "$current_hash" "$publish_dir"
    fi

    echo "  published ${function_name}."
  done < <(find "$scan_dir" -type f -iname '*.csproj' -path '*/src/*' -print0)
done

if [ "$function_count" -eq 0 ]; then
  echo "No Lambda project (<AWSProjectType>Lambda</AWSProjectType>) found under backend/{functions,workflows}/*/src/ — nothing to publish."
else
  echo "Done — checked ${function_count} function(s)."
fi
