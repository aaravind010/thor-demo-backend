#!/usr/bin/env bash
# Creates (or updates) one deploy role for the given environment — deploy-dev,
# deploy-qa, or deploy-prod — replacing thor-shared-role for backend's use.
# thor-shared-role itself is untouched; thor-demo-frontend keeps using it.
#
# Scoped per environment, not shared: broad per-AWS-service actions (ecs:*,
# ecr:*, logs:*) since this repo doesn't hand-scope individual API calls, but
# iam:PassRole is restricted to thor-<environment>-* role ARNs only — that
# naming-scoped restriction is the actual privilege-escalation control, same
# pattern this repo used before consolidating into thor-shared-role.
#
# deploy-qa is trusted by both the qa-candidate and qa GitHub Environments
# (today's two-stage candidate/promote sequence lives in one job graph, but
# still needs the role usable from both environment contexts).
#
# Usage: ./create-deploy-role.sh <environment> <github_org> [aws_profile]

set -euo pipefail

ENVIRONMENT="${1:?Usage: create-deploy-role.sh <environment: dev|qa|prod> <github_org> [aws_profile]}"
GITHUB_ORG="${2:?Usage: create-deploy-role.sh <environment: dev|qa|prod> <github_org> [aws_profile]}"
GITHUB_REPO="${GITHUB_REPO:-thor-demo-backend}"
export AWS_PROFILE="${3:-${AWS_PROFILE:-default}}"

case "${ENVIRONMENT}" in
  dev|qa|prod) ;;
  *) echo "environment must be one of: dev, qa, prod" >&2; exit 1 ;;
esac

# GitHub's immutable subject-claim IDs (org@orgId/repo@repoId), not the
# mutable name-only form — same reasoning as create-shared-role.sh.
GITHUB_ORG_ID="73207164"
GITHUB_REPO_ID="1319058590"

ACCOUNT_ID="$(aws sts get-caller-identity --query Account --output text)"
ROLE_NAME="deploy-${ENVIRONMENT}"
OIDC_PROVIDER_ARN="arn:aws:iam::${ACCOUNT_ID}:oidc-provider/token.actions.githubusercontent.com"

echo "Using AWS profile '${AWS_PROFILE}' -> account ${ACCOUNT_ID}. Creating ${ROLE_NAME}."

if ! aws iam get-open-id-connect-provider --open-id-connect-provider-arn "${OIDC_PROVIDER_ARN}" >/dev/null 2>&1; then
  echo "No GitHub OIDC provider found in this account (${OIDC_PROVIDER_ARN}). Run create-github-oidc-provider.sh first."
  exit 1
fi

REPO_SLUG="${GITHUB_ORG}@${GITHUB_ORG_ID}/${GITHUB_REPO}@${GITHUB_REPO_ID}"

# Branch each environment deploys from (prod ships from main).
case "${ENVIRONMENT}" in
  prod) BRANCH="main" ;;
  *) BRANCH="${ENVIRONMENT}" ;;
esac

# qa trusts both the candidate stage and the promote stage; dev/prod each trust just their one environment.
# Every environment also trusts ref:refs/heads/<branch> for tenant-migrations.yml's generate/plan/
# apply-wait/cancel jobs, which run without an Environment (called from deploy.yml on push, or
# dispatched). Its approve job runs under environment:<env>, already trusted above.
case "${ENVIRONMENT}" in
  qa)
    SUB_JSON='["repo:'"${REPO_SLUG}"':environment:qa-candidate","repo:'"${REPO_SLUG}"':environment:qa"'
    ;;
  *)
    SUB_JSON='["repo:'"${REPO_SLUG}"':environment:'"${ENVIRONMENT}"'"'
    ;;
esac
SUB_JSON="${SUB_JSON}"',"repo:'"${REPO_SLUG}"':ref:refs/heads/'"${BRANCH}"'"]'

TRUST_POLICY="$(jq -n \
  --arg oidc_arn "${OIDC_PROVIDER_ARN}" \
  --argjson subs "${SUB_JSON}" \
  '{
    Version: "2012-10-17",
    Statement: [{
      Effect: "Allow",
      Principal: { Federated: $oidc_arn },
      Action: "sts:AssumeRoleWithWebIdentity",
      Condition: {
        StringEquals: { "token.actions.githubusercontent.com:aud": "sts.amazonaws.com" },
        StringLike:   { "token.actions.githubusercontent.com:sub": $subs }
      }
    }]
  }'
)"

PERMISSIONS_POLICY="$(jq -n \
  --arg env "${ENVIRONMENT}" \
  '{
    Version: "2012-10-17",
    Statement: [
      {
        Sid: "EcrPushPull",
        Effect: "Allow",
        Action: ["ecr:*"],
        Resource: "*"
      },
      {
        Sid: "EcsDeploy",
        Effect: "Allow",
        Action: ["ecs:*"],
        Resource: "*"
      },
      {
        Sid: "Logs",
        Effect: "Allow",
        Action: ["logs:*"],
        Resource: "*"
      },
      {
        Sid: "PassExecutionAndTaskRoles",
        Effect: "Allow",
        Action: ["iam:PassRole"],
        Resource: ("arn:aws:iam::*:role/thor-" + $env + "-*")
      },
      {
        Sid: "TenantMigrationBucket",
        Effect: "Allow",
        Action: ["s3:*"],
        Resource: [
          ("arn:aws:s3:::thor-" + $env + "-tenant-migration-*"),
          ("arn:aws:s3:::thor-" + $env + "-tenant-migration-*/*")
        ]
      },
      {
        Sid: "TenantMigrationExecutions",
        Effect: "Allow",
        Action: ["states:StartExecution", "states:DescribeExecution", "states:StopExecution"],
        Resource: [
          ("arn:aws:states:*:*:stateMachine:thor-" + $env + "-tenant-migration"),
          ("arn:aws:states:*:*:execution:thor-" + $env + "-tenant-migration:*")
        ]
      },
      {
        Sid: "TenantMigrationApproval",
        Effect: "Allow",
        Action: ["states:SendTaskSuccess", "states:SendTaskFailure"],
        Resource: "*"
      }
    ]
  }'
)"

if aws iam get-role --role-name "${ROLE_NAME}" >/dev/null 2>&1; then
  echo "Role ${ROLE_NAME} already exists — updating trust policy and permissions."
  aws iam update-assume-role-policy --role-name "${ROLE_NAME}" --policy-document "${TRUST_POLICY}"
else
  read -rp "Create IAM role ${ROLE_NAME} in account ${ACCOUNT_ID}? [y/N] " CONFIRM
  [[ "${CONFIRM}" =~ ^[Yy]$ ]] || { echo "Aborted."; exit 1; }
  aws iam create-role --role-name "${ROLE_NAME}" --assume-role-policy-document "${TRUST_POLICY}"
fi

aws iam put-role-policy --role-name "${ROLE_NAME}" --policy-name "deploy-${ENVIRONMENT}-permissions" --policy-document "${PERMISSIONS_POLICY}"

ROLE_ARN="$(aws iam get-role --role-name "${ROLE_NAME}" --query 'Role.Arn' --output text)"
echo "Role ready: ${ROLE_ARN}"
if [ "${ENVIRONMENT}" = "qa" ]; then
  echo "Set AWS_ROLE_ARN to this value on both the 'qa-candidate' and 'qa' GitHub Environments."
else
  echo "Set AWS_ROLE_ARN to this value on the '${ENVIRONMENT}' GitHub Environment."
fi
