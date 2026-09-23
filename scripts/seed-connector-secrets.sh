#!/usr/bin/env bash
# Seeds the two connector auth secrets Terraform creates but deliberately leaves empty, for one
# environment: thor-<env>-secret-connector-jwt (the RS256 keypair Thor.Api signs connector JWTs
# with, as JSON {"private_key","public_key"}) and thor-<env>-secret-api-key-pepper (the pepper
# Thor.Api mixes into API-key/refresh-token hashing). Same pattern as the authorizer salt: the
# secret is Terraform's, the value never is — no private key ever reaches a .tf file or tfstate.
#
# Also writes the public half to infra/envs/<env>/connector-jwt-public-key.pem, which is committed
# and read by that environment's terragrunt.hcl. The authorizer Lambda needs the key as a literal
# env var (Lambda has no valueFrom indirection), and that half is public by design — only Thor.Api
# can mint tokens, everything else only verifies them (ADR §5.2).
#
# Run it from anywhere; paths are derived from the script's own location.
#
# Ordering, because this module creates the very secret it seeds:
#   1. terragrunt apply   — creates both secrets, empty
#   2. this script        — generates the keypair, seeds both, writes the .pem
#   3. commit the .pem, terragrunt apply again — the authorizer picks up the public key
# The authorizer stays broken between 1 and 3, which is where it already is. On an environment
# that has already run this once, only step 2 is new work.
#
# Re-running mints a NEW keypair and pepper. That is a rotation, not a no-op: every connector JWT
# and every stored API-key hash issued under the old values stops verifying. See
# infra/bootstrap/connector-secrets.md before doing it to qa or prod.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat >&2 <<'EOF'
Usage: seed-connector-secrets.sh <dev|qa|prod> [-y]

  -y   skip the confirmation prompt (for non-interactive use)

Requires openssl, jq, and an AWS CLI already authenticated against the target
environment's account. dev and qa share an account; prod is separate.
EOF
  exit 2
}

ENVIRONMENT="${1:-}"
ASSUME_YES=false
[ $# -ge 2 ] && [ "${2}" = "-y" ] && ASSUME_YES=true

case "$ENVIRONMENT" in
  dev|qa|prod) ;;
  *) usage ;;
esac

for tool in openssl jq aws; do
  command -v "$tool" >/dev/null 2>&1 || { echo "Need ${tool} on PATH — not found." >&2; exit 1; }
done

ENV_DIR="${REPO_ROOT}/infra/envs/${ENVIRONMENT}"
[ -d "$ENV_DIR" ] || { echo "No such environment directory: ${ENV_DIR}" >&2; exit 1; }

PUBLIC_KEY_FILE="${ENV_DIR}/connector-jwt-public-key.pem"
JWT_SECRET="thor-${ENVIRONMENT}-secret-connector-jwt"
PEPPER_SECRET="thor-${ENVIRONMENT}-secret-api-key-pepper"

# Fail before generating anything if the secrets aren't there — the alternative is creating them
# here, which would leave Terraform wanting to create a secret that already exists on every apply.
for secret in "$JWT_SECRET" "$PEPPER_SECRET"; do
  if ! aws secretsmanager describe-secret --secret-id "$secret" >/dev/null 2>&1; then
    cat >&2 <<EOF
Secret ${secret} does not exist in this account.

Terraform owns these secrets (infra/src/modules/secrets). Run a terragrunt apply for
${ENVIRONMENT} first, then re-run this script. If the apply already ran, check that your AWS
credentials point at the ${ENVIRONMENT} account.
EOF
    exit 1
  fi
done

ACCOUNT_ID="$(aws sts get-caller-identity --query Account --output text)"
CALLER_ARN="$(aws sts get-caller-identity --query Arn --output text)"

ROTATION_WARNING=""
if [ -f "$PUBLIC_KEY_FILE" ]; then
  ROTATION_WARNING="
  !! ${PUBLIC_KEY_FILE#"${REPO_ROOT}/"} already exists, so this is a ROTATION.
     Every connector JWT and every stored API-key hash issued under the current
     values will stop verifying. Connectors must re-register."
fi

cat <<EOF

Seeding connector auth secrets for ${ENVIRONMENT}:

  account   ${ACCOUNT_ID}
  caller    ${CALLER_ARN}
  secrets   ${JWT_SECRET}
            ${PEPPER_SECRET}
  writes    ${PUBLIC_KEY_FILE#"${REPO_ROOT}/"}
${ROTATION_WARNING}
EOF

if [ "$ASSUME_YES" != "true" ]; then
  read -r -p "Proceed? [y/N] " reply
  case "$reply" in
    y|Y) ;;
    *) echo "Aborted."; exit 1 ;;
  esac
fi

# Everything secret lives here and nowhere else. The trap covers the error paths too, since
# set -e means any failure below exits straight through it.
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT
chmod 700 "$WORK_DIR"

# Under Git Bash the AWS CLI is a native Windows binary and can't resolve an MSYS path like
# /tmp/tmp.X, so file:// paramfiles need the Windows form. cygpath -m keeps forward slashes,
# which is what the CLI wants after the file:// prefix. Elsewhere this is a no-op.
if command -v cygpath >/dev/null 2>&1; then
  PARAM_DIR="$(cygpath -m "$WORK_DIR")"
else
  PARAM_DIR="$WORK_DIR"
fi

echo "Generating RSA-2048 keypair..."
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "${WORK_DIR}/private.pem" 2>/dev/null
openssl rsa -pubout -in "${WORK_DIR}/private.pem" -out "${WORK_DIR}/public.pem" 2>/dev/null

# -R reads raw lines, -s slurps them into one string: the PEM's newlines survive as \n in JSON,
# which is what RSA.ImportFromPem expects to read back.
jq -Rs --rawfile pub "${WORK_DIR}/public.pem" \
  '{private_key: ., public_key: $pub}' \
  < "${WORK_DIR}/private.pem" > "${WORK_DIR}/jwt-payload.json"

echo "Writing ${JWT_SECRET}..."
aws secretsmanager put-secret-value \
  --secret-id "$JWT_SECRET" \
  --secret-string "file://${PARAM_DIR}/jwt-payload.json" \
  --query 'VersionId' --output text >/dev/null

echo "Writing ${PEPPER_SECRET}..."
# printf, not a plain redirect: openssl appends a newline, and the pepper is concatenated with
# key material verbatim — a trailing \n would silently become part of every hash.
printf '%s' "$(openssl rand -base64 32)" > "${WORK_DIR}/pepper.txt"
aws secretsmanager put-secret-value \
  --secret-id "$PEPPER_SECRET" \
  --secret-string "file://${PARAM_DIR}/pepper.txt" \
  --query 'VersionId' --output text >/dev/null

cp "${WORK_DIR}/public.pem" "$PUBLIC_KEY_FILE"
echo "Wrote ${PUBLIC_KEY_FILE#"${REPO_ROOT}/"}."

cat <<EOF

Done. The private key and pepper exist only in Secrets Manager — nothing sensitive was
written to disk outside a temp dir that is now deleted.

Next:
  1. git add ${PUBLIC_KEY_FILE#"${REPO_ROOT}/"} && commit
  2. terragrunt apply for ${ENVIRONMENT} — puts the public key on the authorizer Lambda
  3. force a new ECS deployment of thor-api and task-api so they pick up the seeded secrets
     (ECS resolves valueFrom at task start; a running task keeps the value it started with)
EOF
