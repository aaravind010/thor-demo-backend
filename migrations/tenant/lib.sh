# shellcheck shell=sh
# Shared helpers for the runner's inspect.sh and apply.sh. Sourced, not executed.
#
# Required env (set on the task definition by infra/src/modules/tenant_migration):
#   THOR_MIGRATION_BUCKET   plan bucket
#   THOR_AWS_REGION         region for RDS IAM tokens (Fargate doesn't set AWS_REGION)
#   THOR_MASTERDB_HOST      RDS Proxy endpoint
#   THOR_MASTERDB_DATABASE  master DB name
#   THOR_MASTERDB_USER      thor_app — already has read access to auth.tenant / auth.tenant_routing
#   THOR_DB_PORT            5432
#
# Auth is RDS IAM end to end (no passwords anywhere): every connection mints a token under the
# task role's rds-db:connect grant.

set -eu

TAB=$(printf '\t')
TENANT_STATUS_ACTIVE=2 # Thor.TenantProvisioning TenantStatus.Active

log() { printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >&2; }
die() { log "ERROR: $*"; exit 1; }

input() { printf '%s' "$THOR_INPUT" | jq -er "$1"; }

iam_token() { # host user
  aws rds generate-db-auth-token --hostname "$1" --port "$THOR_DB_PORT" \
    --username "$2" --region "$THOR_AWS_REGION"
}

s3_get() { aws s3 cp --only-show-errors "s3://$THOR_MIGRATION_BUCKET/$1" "$2"; }
s3_put() { aws s3 cp --only-show-errors "$1" "s3://$THOR_MIGRATION_BUCKET/$2"; }

sha256() { sha256sum "$1" | cut -d' ' -f1; }

# Prints "tenant_id<TAB>cluster_endpoint<TAB>database_name<TAB>db_user" for every active tenant,
# or just the one asked for. The routing row is the only source of where a tenant lives — never a
# hard-coded host or DB name.
resolve_tenants() { # all | <tenant_id>
  PGPASSWORD=$(iam_token "$THOR_MASTERDB_HOST" "$THOR_MASTERDB_USER") psql -X -q -At -F "$TAB" \
    -v ON_ERROR_STOP=1 -v tenant="$1" -v active="$TENANT_STATUS_ACTIVE" \
    "host=$THOR_MASTERDB_HOST port=$THOR_DB_PORT dbname=$THOR_MASTERDB_DATABASE user=$THOR_MASTERDB_USER sslmode=require" <<'SQL'
SELECT t.tenant_id, r.cluster_endpoint, r.database_name, r.db_user
FROM auth.tenant t
JOIN auth.tenant_routing r ON r.tenant_id = t.tenant_id
WHERE t.status_id = :active
  AND (:'tenant' = 'all' OR t.tenant_id::text = :'tenant')
ORDER BY t.tenant_id;
SQL
}

tenant_psql() { # host db user token, then psql args
  _host=$1 _db=$2 _user=$3 _token=$4
  shift 4
  PGPASSWORD=$_token psql -X -q -v ON_ERROR_STOP=1 \
    "host=$_host port=$THOR_DB_PORT dbname=$_db user=$_user sslmode=require" "$@"
}

# The tenant schema's live state as HCL. Inspection needs no dev database (only diffing does, and
# that happens in CI), and its output is deterministic, so its hash detects drift between plan and
# apply. EF's own history table isn't part of the model, so it's left out.
atlas_inspect() { # host db user token out
  _url=$(printf 'postgres://%s:%s@%s:%s/%s?sslmode=require' \
    "$3" "$(printf '%s' "$4" | jq -sRr @uri)" "$1" "$THOR_DB_PORT" "$2") # token contains & = %
  atlas schema inspect --url "$_url" --schema tenant --exclude 'tenant.__EFMigrationsHistory' \
    --format '{{ hcl . }}' > "$5"
}
