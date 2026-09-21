#!/usr/bin/env bash
# Lists (does NOT delete) every AWS resource tagged Project=thor-platform / Environment=<env> in
# this account, grouped by service, excluding IAM entirely. Read-only — every call here is a
# list/describe/get, nothing that modifies state. Run this, review the output, then decide what
# actually needs manual/scripted deletion before re-running terragrunt apply.
#
# Usage: ./find-orphaned-thor-resources.sh [environment]   (default: dev)
#
# Requires: aws CLI (configured/authenticated against the target account), jq.

set -euo pipefail

ENVIRONMENT="${1:-dev}"
PROJECT_TAG="thor-platform"

echo "=== Scanning for Project=${PROJECT_TAG}, Environment=${ENVIRONMENT} resources ==="
echo "(IAM excluded — this script never touches roles/policies/boundaries)"
echo

command -v jq >/dev/null 2>&1 || { echo "jq is required but not found." >&2; exit 1; }
command -v aws >/dev/null 2>&1 || { echo "aws CLI is required but not found." >&2; exit 1; }

CALLER_ACCOUNT="$(aws sts get-caller-identity --query Account --output text)"
echo "Authenticated against account: ${CALLER_ACCOUNT}"
echo

# --- Primary sweep: Resource Groups Tagging API ---
# Covers most of what this repo manages (EC2/VPC, ECS, ECR, ELB, RDS, Neptune — same "rds" service
# namespace, see below — S3, SQS, Lambda, EventBridge Pipes, Step Functions, Secrets Manager,
# CloudWatch Logs, API Gateway, CloudFront, WAFv2, ACM) in one call, grouped by service.
echo "--- Resource Groups Tagging API sweep ---"
TAGGED_JSON="$(aws resourcegroupstaggingapi get-resources \
  --tag-filters "Key=Project,Values=${PROJECT_TAG}" "Key=Environment,Values=${ENVIRONMENT}" \
  --output json)"

echo "$TAGGED_JSON" | jq -r '
  .ResourceTagMappingList[]
  | .ResourceARN
  | select(startswith("arn:aws:iam::") | not)
  | (split(":")[2]) as $service
  | "\($service)\t\(.)"
' | sort | awk -F'\t' '
  $1 != prev { print "\n[" $1 "]"; prev = $1 }
  { print "  " $2 }
'

TAGGED_COUNT="$(echo "$TAGGED_JSON" | jq '[.ResourceTagMappingList[] | select(.ResourceARN | startswith("arn:aws:iam::") | not)] | length')"
echo
echo "Tagged resources found (excluding IAM): ${TAGGED_COUNT}"
echo

# Note on Neptune vs Aurora: both live under the "rds" service namespace (arn:aws:rds:...) since
# Neptune shares Amazon RDS's ARN/action format — see project_neptune_iam_permissions memory. The
# [rds] section above will list both together; distinguish by the resource id
# (thor-<env>-aurora* vs thor-<env>-neptune*).

# --- Supplementary lookup 1: Route53 hosted zones ---
# Route53 zone tagging isn't reliably surfaced by resourcegroupstaggingapi — checked directly by
# name instead. Lists the zone and (separately) any non-NS/SOA records still in it, since those
# block zone deletion (HostedZoneNotEmpty) until cleared.
echo "--- Route53 hosted zones (name-matched, not tag-matched) ---"
ZONE_IDS="$(aws route53 list-hosted-zones --output json | jq -r --arg env "$ENVIRONMENT" '
  .HostedZones[]
  | select(.Name | test("^(" + $env + "\\.)?cndemo\\.com\\.$"))
  | .Id
')"

if [ -z "$ZONE_IDS" ]; then
  echo "  (none matching *${ENVIRONMENT}*.cndemo.com or cndemo.com apex)"
else
  for zid in $ZONE_IDS; do
    zname="$(aws route53 get-hosted-zone --id "$zid" --query 'HostedZone.Name' --output text)"
    echo "  Zone: $zname ($zid)"
    aws route53 list-resource-record-sets --hosted-zone-id "$zid" --output json \
      | jq -r '.ResourceRecordSets[] | select(.Type != "NS" and .Type != "SOA") | "    record: \(.Name) (\(.Type))"'
  done
fi
echo

# --- Supplementary lookup 2: Aurora's own Secrets Manager secret ---
# manage_master_user_password gives the secret an AWS-generated name (rds!cluster-<id>-<suffix>),
# not a thor-<env>-* name, so it won't match a name-based grep and may not carry our custom tags —
# found via the owning cluster instead. Deleted automatically when the cluster is deleted, not
# independently, so this is informational only (confirms it exists / where it's tied).
echo "--- Aurora-managed master secret (tied to thor-${ENVIRONMENT}-aurora, not separately named) ---"
aws rds describe-db-clusters --db-cluster-identifier "thor-${ENVIRONMENT}-aurora" --output json 2>/dev/null \
  | jq -r '.DBClusters[] | "  \(.MasterUserSecret.SecretArn // "none") (status: \(.MasterUserSecret.SecretStatus // "n/a"))"' \
  || echo "  (cluster thor-${ENVIRONMENT}-aurora not found — already deleted, or never existed)"
echo

echo "=== Done. Nothing was deleted — review the list above before taking any destructive action. ==="
