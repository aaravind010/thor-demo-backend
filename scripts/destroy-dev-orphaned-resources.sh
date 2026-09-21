#!/usr/bin/env bash
# Deletes the specific thor-dev-tagged AWS resources found by
# find-orphaned-thor-resources.sh dev (run 2026-09-08), so `terragrunt apply`
# can rebuild dev cleanly from the new (empty) state bucket. IAM is never
# touched — no iam:* call anywhere in this script.
#
# Deliberately NOT included: the two Route53 hosted zones (dev.cndemo.com,
# cndemo.com). Route53 allows duplicate zone names without erroring, so they
# aren't actually the cause of any "already exists" failure — and deleting +
# recreating a zone changes its NS records, which breaks real DNS delegation
# if the domain registrar points at the current zone. Handle those two
# separately (most likely: terraform import once the new state exists).
#
# This is a ONE-TIME cleanup script for the specific resource IDs found in
# the 2026-09-08 scan — not a generic reusable tool. Re-run
# find-orphaned-thor-resources.sh first if using this later; the IDs below
# may no longer be accurate.
#
# Usage: ./destroy-dev-orphaned-resources.sh

set -uo pipefail  # not -e: one failed delete shouldn't abort the whole run

REGION="us-east-1"
FAILURES=0

log()  { echo ">>> $*"; }
fail() { echo "!!! FAILED: $*" >&2; FAILURES=$((FAILURES + 1)); }

echo "This will DELETE the following from AWS account $(aws sts get-caller-identity --query Account --output text):"
echo "  - CloudFront distribution E1EJWHMDVR3N9W (disable + wait, then delete)"
echo "  - WAFv2 web ACL thor-dev-frontend-waf"
echo "  - EventBridge Pipe thor-dev-ingestion-pipe"
echo "  - Step Functions state machine thor-dev-ingestion-sf"
echo "  - ECS clusters: thor-dev, thor-dev-cluster, thor-dev-ingestion-cluster (+ all task def revisions)"
echo "  - RDS: thor-dev-aurora (2 instances + cluster + subnet group)"
echo "  - Neptune: thor-dev-neptune (2 instances + cluster + subnet group)"
echo "  - S3 buckets: thor-dev-ingestion-877969058937, thor-frontend-dev-877969058937 (emptied then deleted)"
echo "  - SQS: thor-dev-ingestion-sqs, thor-dev-ingestion-dlq"
echo "  - Lambda: thor-dev-api-key-authorizer, thor-dev-ingestion-create-manifest"
echo "  - ECR repository: thor-dev-ingestion-ecr (force, including any images)"
echo "  - CloudWatch log groups (7, listed in the scan)"
echo "  - Secrets Manager: thor-dev-authorizer-salt-*, thor-dev-thor-api-tls-sidecar-cert-*"
echo "    (NOT the rds!cluster-* secret — that's deleted automatically with the Aurora cluster)"
echo "  - Service Discovery namespace ns-abarmfu3bdggeqzn"
echo "  - ACM certificates (6, listed in the scan — deleted after CloudFront is gone)"
echo "  - Both VPCs and everything in them (subnets, route tables, security groups, VPC endpoints)"
echo
echo "NOT touched: IAM (anything), Route53 zones/records."
echo
read -rp "Type DELETE (all caps) to proceed: " CONFIRM
[ "${CONFIRM}" = "DELETE" ] || { echo "Aborted — no changes made."; exit 1; }

# ---------------------------------------------------------------------------
log "CloudFront: disabling E1EJWHMDVR3N9W"
CF_ID="E1EJWHMDVR3N9W"
CF_CONFIG_JSON="$(aws cloudfront get-distribution-config --id "$CF_ID" 2>/dev/null)"
if [ -n "$CF_CONFIG_JSON" ]; then
  CF_ETAG="$(echo "$CF_CONFIG_JSON" | jq -r '.ETag')"
  echo "$CF_CONFIG_JSON" | jq '.DistributionConfig | .Enabled = false' > /tmp/cf-disabled-config.json
  aws cloudfront update-distribution --id "$CF_ID" --if-match "$CF_ETAG" \
    --distribution-config file:///tmp/cf-disabled-config.json >/dev/null \
    && log "  disable requested, waiting for deployment (this can take 15-20 min)..." \
    && aws cloudfront wait distribution-deployed --id "$CF_ID" \
    && CF_ETAG2="$(aws cloudfront get-distribution --id "$CF_ID" --query 'ETag' --output text)" \
    && aws cloudfront delete-distribution --id "$CF_ID" --if-match "$CF_ETAG2" \
    && log "  deleted CloudFront distribution ${CF_ID}" \
    || fail "CloudFront distribution ${CF_ID}"
else
  log "  ${CF_ID} not found — already gone, skipping"
fi

# ---------------------------------------------------------------------------
log "WAFv2: deleting thor-dev-frontend-waf"
WAF_ID="62f3886a-dbc0-42f5-bd37-961571e8b215"
WAF_LOCK="$(aws wafv2 get-web-acl --name thor-dev-frontend-waf --scope CLOUDFRONT --id "$WAF_ID" \
  --region us-east-1 --query 'LockToken' --output text 2>/dev/null)"
if [ -n "$WAF_LOCK" ] && [ "$WAF_LOCK" != "None" ]; then
  aws wafv2 delete-web-acl --name thor-dev-frontend-waf --scope CLOUDFRONT --id "$WAF_ID" \
    --lock-token "$WAF_LOCK" --region us-east-1 \
    && log "  deleted" || fail "WAFv2 web ACL thor-dev-frontend-waf"
else
  log "  not found — already gone, skipping"
fi

# ---------------------------------------------------------------------------
log "EventBridge Pipes: deleting thor-dev-ingestion-pipe"
aws pipes delete-pipe --name thor-dev-ingestion-pipe --region "$REGION" >/dev/null 2>&1 \
  && log "  deleted" || log "  not found or already deleting — skipping"

# ---------------------------------------------------------------------------
log "Step Functions: deleting thor-dev-ingestion-sf"
SFN_ARN="arn:aws:states:${REGION}:$(aws sts get-caller-identity --query Account --output text):stateMachine:thor-dev-ingestion-sf"
aws stepfunctions delete-state-machine --state-machine-arn "$SFN_ARN" --region "$REGION" >/dev/null 2>&1 \
  && log "  deletion requested" || log "  not found — skipping"

# ---------------------------------------------------------------------------
log "ECS: deregistering all task definition revisions"
for family in thor-dev-ingestion-extract-stage thor-dev-ingestion-graph-load-poll \
              thor-dev-ingestion-graph-load-start thor-dev-ingestion-promote \
              thor-dev-intelligence-engine thor-dev-task-api thor-dev-thor-api thor-dev-thor; do
  revisions="$(aws ecs list-task-definitions --family-prefix "$family" --region "$REGION" \
    --query 'taskDefinitionArns' --output text 2>/dev/null)"
  for arn in $revisions; do
    aws ecs deregister-task-definition --task-definition "$arn" --region "$REGION" >/dev/null 2>&1 \
      && aws ecs delete-task-definitions --task-definitions "$arn" --region "$REGION" >/dev/null 2>&1
  done
  log "  ${family}: deregistered ${revisions:+$(echo "$revisions" | wc -w | tr -d ' ')} revision(s)"
done

log "ECS: deleting clusters"
for cluster in thor-dev thor-dev-cluster thor-dev-ingestion-cluster; do
  aws ecs delete-cluster --cluster "$cluster" --region "$REGION" >/dev/null 2>&1 \
    && log "  deleted ${cluster}" || log "  ${cluster} not found or not empty — check manually"
done

# ---------------------------------------------------------------------------
log "RDS: deleting Aurora instances"
for inst in thor-dev-aurora-instance thor-dev-aurora-instance-2; do
  aws rds delete-db-instance --db-instance-identifier "$inst" --region "$REGION" >/dev/null 2>&1 \
    && log "  deletion requested: ${inst}" || log "  ${inst} not found — skipping"
done
log "  waiting for Aurora instances to finish deleting..."
aws rds wait db-instance-deleted --db-instance-identifier thor-dev-aurora-instance --region "$REGION" 2>/dev/null
aws rds wait db-instance-deleted --db-instance-identifier thor-dev-aurora-instance-2 --region "$REGION" 2>/dev/null

log "RDS: deleting Aurora cluster thor-dev-aurora"
aws rds delete-db-cluster --db-cluster-identifier thor-dev-aurora --skip-final-snapshot --region "$REGION" >/dev/null 2>&1 \
  && aws rds wait db-cluster-deleted --db-cluster-identifier thor-dev-aurora --region "$REGION" 2>/dev/null \
  && log "  deleted" || fail "Aurora cluster thor-dev-aurora"

log "RDS: deleting Aurora subnet group"
SUBGRP="$(aws rds describe-db-subnet-groups --region "$REGION" \
  --query "DBSubnetGroups[?starts_with(DBSubnetGroupName, 'thor-dev-aurora-subnet-group')].DBSubnetGroupName" \
  --output text 2>/dev/null)"
[ -n "$SUBGRP" ] && aws rds delete-db-subnet-group --db-subnet-group-name "$SUBGRP" --region "$REGION" \
  && log "  deleted ${SUBGRP}" || log "  not found — skipping"

# ---------------------------------------------------------------------------
log "Neptune: deleting instances"
for inst in thor-dev-neptune-instance thor-dev-neptune-instance-2; do
  aws rds delete-db-instance --db-instance-identifier "$inst" --region "$REGION" >/dev/null 2>&1 \
    && log "  deletion requested: ${inst}" || log "  ${inst} not found — skipping"
done
log "  waiting for Neptune instances to finish deleting..."
aws rds wait db-instance-deleted --db-instance-identifier thor-dev-neptune-instance --region "$REGION" 2>/dev/null
aws rds wait db-instance-deleted --db-instance-identifier thor-dev-neptune-instance-2 --region "$REGION" 2>/dev/null

log "Neptune: deleting cluster thor-dev-neptune"
aws rds delete-db-cluster --db-cluster-identifier thor-dev-neptune --skip-final-snapshot --region "$REGION" >/dev/null 2>&1 \
  && aws rds wait db-cluster-deleted --db-cluster-identifier thor-dev-neptune --region "$REGION" 2>/dev/null \
  && log "  deleted" || fail "Neptune cluster thor-dev-neptune"

log "Neptune: deleting subnet group"
aws rds delete-db-subnet-group --db-subnet-group-name thor-dev-neptune --region "$REGION" >/dev/null 2>&1 \
  && log "  deleted" || log "  not found — skipping"

# ---------------------------------------------------------------------------
log "S3: emptying and deleting buckets"
for bucket in thor-dev-ingestion-877969058937 thor-frontend-dev-877969058937; do
  if aws s3api head-bucket --bucket "$bucket" 2>/dev/null; then
    aws s3api list-object-versions --bucket "$bucket" --output json \
      | jq -c '[(.Versions // [])[], (.DeleteMarkers // [])[]] | .[] | {Key: .Key, VersionId: .VersionId}' \
      | while read -r obj; do
          key="$(echo "$obj" | jq -r '.Key')"
          vid="$(echo "$obj" | jq -r '.VersionId')"
          aws s3api delete-object --bucket "$bucket" --key "$key" --version-id "$vid" >/dev/null 2>&1
        done
    aws s3 rb "s3://${bucket}" --force \
      && log "  deleted ${bucket}" || fail "S3 bucket ${bucket}"
  else
    log "  ${bucket} not found — skipping"
  fi
done

# ---------------------------------------------------------------------------
log "SQS: deleting queues"
for q in thor-dev-ingestion-sqs thor-dev-ingestion-dlq; do
  url="$(aws sqs get-queue-url --queue-name "$q" --region "$REGION" --query QueueUrl --output text 2>/dev/null)"
  [ -n "$url" ] && aws sqs delete-queue --queue-url "$url" --region "$REGION" \
    && log "  deleted ${q}" || log "  ${q} not found — skipping"
done

# ---------------------------------------------------------------------------
log "Lambda: deleting functions"
for fn in thor-dev-api-key-authorizer thor-dev-ingestion-create-manifest; do
  aws lambda delete-function --function-name "$fn" --region "$REGION" >/dev/null 2>&1 \
    && log "  deleted ${fn}" || log "  ${fn} not found — skipping"
done

# ---------------------------------------------------------------------------
log "ECR: force-deleting thor-dev-ingestion-ecr"
aws ecr delete-repository --repository-name thor-dev-ingestion-ecr --force --region "$REGION" >/dev/null 2>&1 \
  && log "  deleted" || log "  not found — skipping"

# ---------------------------------------------------------------------------
log "CloudWatch Logs: deleting log groups"
for lg in "/aws/lambda/thor-dev-api-key-authorizer" "/aws/lambda/thor-dev-ingestion-create-manifest" \
          "/aws/states/thor-dev-ingestion" "/ecs/dev/intelligence-engine" "/ecs/dev/task-api" \
          "/ecs/dev/thor-api" "/ecs/dev/thor-dev-ingestion"; do
  aws logs delete-log-group --log-group-name "$lg" --region "$REGION" >/dev/null 2>&1 \
    && log "  deleted ${lg}" || log "  ${lg} not found — skipping"
done

# ---------------------------------------------------------------------------
log "Secrets Manager: force-deleting non-RDS-managed secrets"
for secret in thor-dev-authorizer-salt thor-dev-thor-api-tls-sidecar-cert; do
  arn="$(aws secretsmanager list-secrets --region "$REGION" \
    --query "SecretList[?starts_with(Name, '${secret}')].ARN" --output text 2>/dev/null)"
  [ -n "$arn" ] && aws secretsmanager delete-secret --secret-id "$arn" --force-delete-without-recovery --region "$REGION" >/dev/null \
    && log "  deleted ${secret}" || log "  ${secret} not found — skipping"
done

# ---------------------------------------------------------------------------
log "Service Discovery: deleting namespace"
aws servicediscovery delete-namespace --id ns-abarmfu3bdggeqzn --region "$REGION" >/dev/null 2>&1 \
  && log "  deletion requested" || log "  not found — skipping"

# ---------------------------------------------------------------------------
log "ACM: deleting certificates"
for cert_arn in \
  "arn:aws:acm:us-east-1:877969058937:certificate/00ab4145-84ac-4176-8e4f-4a66f7000b49" \
  "arn:aws:acm:us-east-1:877969058937:certificate/6b845780-864a-4ab1-bc36-7d4d16d54160" \
  "arn:aws:acm:us-east-1:877969058937:certificate/733977cc-6352-4a8f-a881-9d98d0c747bc" \
  "arn:aws:acm:us-east-1:877969058937:certificate/9ffe3fbd-bc82-4ddc-a6c9-969bd55f4e7b" \
  "arn:aws:acm:us-east-1:877969058937:certificate/cd332a9f-6cea-42e5-bed8-ef029fa4f9f7" \
  "arn:aws:acm:us-east-1:877969058937:certificate/e82be25f-77e9-431a-851d-c35c3751a843"; do
  aws acm delete-certificate --certificate-arn "$cert_arn" --region us-east-1 >/dev/null 2>&1 \
    && log "  deleted ${cert_arn##*/}" || fail "ACM certificate ${cert_arn} (likely still in use by something — check manually)"
done

# ---------------------------------------------------------------------------
log "EC2: deleting VPC endpoints, then security groups, subnets, route tables, VPCs"
for vpc in vpc-0598ad95a91121603 vpc-076adac253ac64375; do
  log "  VPC ${vpc}:"

  eps="$(aws ec2 describe-vpc-endpoints --filters "Name=vpc-id,Values=${vpc}" --region "$REGION" \
    --query 'VpcEndpoints[].VpcEndpointId' --output text)"
  [ -n "$eps" ] && aws ec2 delete-vpc-endpoints --vpc-endpoint-ids $eps --region "$REGION" >/dev/null \
    && log "    deleted VPC endpoints: ${eps}"

  sgs="$(aws ec2 describe-security-groups --filters "Name=vpc-id,Values=${vpc}" --region "$REGION" \
    --query "SecurityGroups[?GroupName!='default'].GroupId" --output text)"
  for sg in $sgs; do
    aws ec2 delete-security-group --group-id "$sg" --region "$REGION" >/dev/null 2>&1 \
      && log "    deleted SG ${sg}" || log "    SG ${sg} still referenced — check manually"
  done

  subnets="$(aws ec2 describe-subnets --filters "Name=vpc-id,Values=${vpc}" --region "$REGION" \
    --query 'Subnets[].SubnetId' --output text)"
  for subnet in $subnets; do
    aws ec2 delete-subnet --subnet-id "$subnet" --region "$REGION" >/dev/null 2>&1 \
      && log "    deleted subnet ${subnet}" || log "    subnet ${subnet} still in use — check manually"
  done

  rts="$(aws ec2 describe-route-tables --filters "Name=vpc-id,Values=${vpc}" --region "$REGION" \
    --query "RouteTables[?length(Associations[?Main==\`true\`]) == \`0\`].RouteTableId" --output text)"
  for rt in $rts; do
    aws ec2 delete-route-table --route-table-id "$rt" --region "$REGION" >/dev/null 2>&1 \
      && log "    deleted route table ${rt}" || log "    route table ${rt} still associated — check manually"
  done

  aws ec2 delete-vpc --vpc-id "$vpc" --region "$REGION" >/dev/null 2>&1 \
    && log "    deleted VPC ${vpc}" || fail "VPC ${vpc} — something is still attached, check the console"
done

# ---------------------------------------------------------------------------
echo
if [ "$FAILURES" -eq 0 ]; then
  echo "=== Done. Everything above was deleted (or already gone). ==="
else
  echo "=== Done with ${FAILURES} failure(s) — see 'FAILED:' lines above and check those manually. ==="
fi
echo "Route53 zones (dev.cndemo.com, cndemo.com) were NOT touched — handle separately."
