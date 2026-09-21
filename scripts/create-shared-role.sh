#!/usr/bin/env bash
# Creates (or updates) ONE IAM role, with AWS-managed AdministratorAccess,
# shared by every GitHub Actions workflow across both repos — replaces the
# former create-deploy-role.sh (app deploys) and create-terraform-role.sh
# (Terraform/Terragrunt) entirely. There's nothing left to scope once
# permissions are AdministratorAccess, so the only thing left for this
# script to do is manage the trust policy (which GitHub Environments, across
# which repos, may assume this role).
#
# This only works because dev/qa/prod currently all live in the same AWS
# account (prod's account ID is still an empty placeholder in
# infra/root.hcl's account_map). An IAM role lives in exactly one account —
# the moment a real, separate prod account exists, this role becomes
# unusable there regardless of trust policy, and prod would need its own
# role in that account again, for both app deploys and Terraform.
# Consolidating now is a deliberate, temporary simplification, not a
# permanent architecture change.
#
# AdministratorAccess was an explicit choice, made after being warned it's a
# real risk for an OIDC-assumed CI role: every workflow run pulls in
# third-party actions (hashicorp/setup-terraform,
# aws-actions/configure-aws-credentials, terraform-linters/setup-tflint,
# dorny/paths-filter, etc.) — a compromise of any of those means full
# account takeover, not just this project's resources, since nothing here
# is scoped by resource name anymore.
#
# Trusted GitHub Environments, across both repos:
#   thor-demo-backend: dev, qa-candidate, qa, prod (app deploy — backend.yml)
#                       dev-infra-plan, dev-infra, qa-infra-plan, qa-infra,
#                       prod-infra-plan, prod-infra (Terraform — infra.yml)
#   thor-demo-frontend: dev, qa, prod (app deploy — deploy.yml; no infra
#                       pipeline of its own)
#
# Usage: ./create-shared-role.sh <github_org> [aws_profile]

set -euo pipefail

GITHUB_ORG="${1:?Usage: create-shared-role.sh <github_org> [aws_profile]}"
BACKEND_GITHUB_REPO="${GITHUB_REPO:-thor-demo-backend}"
FRONTEND_GITHUB_REPO="${FRONTEND_GITHUB_REPO:-thor-demo-frontend}"
export AWS_PROFILE="${2:-${AWS_PROFILE:-default}}"

# GitHub's immutable subject-claim IDs (org@orgId/repo@repoId, not the
# mutable name-only form) — org/repo names can be renamed or reclaimed by
# someone else after a delete, but these numeric IDs can't be reused, so
# the trust policy stays correct even through a rename. Same org ID for
# both repos; each repo has its own ID.
GITHUB_ORG_ID="73207164"
BACKEND_GITHUB_REPO_ID="1319058590"
FRONTEND_GITHUB_REPO_ID="1321100416"

INFRA_ENVIRONMENTS=(dev qa prod)

ACCOUNT_ID="$(aws sts get-caller-identity --query Account --output text)"
ROLE_NAME="thor-shared-role"
OIDC_PROVIDER_ARN="arn:aws:iam::${ACCOUNT_ID}:oidc-provider/token.actions.githubusercontent.com"

echo "Using AWS profile '${AWS_PROFILE}' -> account ${ACCOUNT_ID}. Creating one shared role for ${BACKEND_GITHUB_REPO} and ${FRONTEND_GITHUB_REPO}."

if ! aws iam get-open-id-connect-provider --open-id-connect-provider-arn "${OIDC_PROVIDER_ARN}" >/dev/null 2>&1; then
  echo "No GitHub OIDC provider found in this account (${OIDC_PROVIDER_ARN}). Run create-github-oidc-provider.sh first."
  exit 1
fi

BACKEND_REPO_SLUG="${GITHUB_ORG}@${GITHUB_ORG_ID}/${BACKEND_GITHUB_REPO}@${BACKEND_GITHUB_REPO_ID}"
FRONTEND_REPO_SLUG="${GITHUB_ORG}@${GITHUB_ORG_ID}/${FRONTEND_GITHUB_REPO}@${FRONTEND_GITHUB_REPO_ID}"

SUB_JSON="$(
  {
    # Backend app-deploy environments (backend.yml / backend-dispatch.yml).
    printf '%s\n' "repo:${BACKEND_REPO_SLUG}:environment:dev"
    printf '%s\n' "repo:${BACKEND_REPO_SLUG}:environment:qa-candidate"
    printf '%s\n' "repo:${BACKEND_REPO_SLUG}:environment:qa"
    printf '%s\n' "repo:${BACKEND_REPO_SLUG}:environment:prod"

    # Backend Terraform environments (infra.yml), both plan and apply, per environment.
    for env in "${INFRA_ENVIRONMENTS[@]}"; do
      printf '%s\n' "repo:${BACKEND_REPO_SLUG}:environment:${env}-infra"
      printf '%s\n' "repo:${BACKEND_REPO_SLUG}:environment:${env}-infra-plan"
    done

    # Frontend app-deploy environments (deploy.yml) — no candidate split, no infra pipeline.
    printf '%s\n' "repo:${FRONTEND_REPO_SLUG}:environment:dev"
    printf '%s\n' "repo:${FRONTEND_REPO_SLUG}:environment:qa"
    printf '%s\n' "repo:${FRONTEND_REPO_SLUG}:environment:prod"
  } | jq -R . | jq -s .
)"

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

ADMIN_POLICY_ARN="arn:aws:iam::aws:policy/AdministratorAccess"

if aws iam get-role --role-name "${ROLE_NAME}" >/dev/null 2>&1; then
  echo "Role ${ROLE_NAME} already exists — updating trust policy and permissions."
  aws iam update-assume-role-policy --role-name "${ROLE_NAME}" --policy-document "${TRUST_POLICY}"
else
  read -rp "Create IAM role ${ROLE_NAME} in account ${ACCOUNT_ID}? [y/N] " CONFIRM
  [[ "${CONFIRM}" =~ ^[Yy]$ ]] || { echo "Aborted."; exit 1; }
  aws iam create-role --role-name "${ROLE_NAME}" --assume-role-policy-document "${TRUST_POLICY}"
fi

aws iam attach-role-policy --role-name "${ROLE_NAME}" --policy-arn "${ADMIN_POLICY_ARN}"

ROLE_ARN="$(aws iam get-role --role-name "${ROLE_NAME}" --query 'Role.Arn' --output text)"
echo "Role ready: ${ROLE_ARN}"
echo "Set AWS_ROLE_ARN to this value on EVERY GitHub Environment listed above, in both thor-demo-backend and thor-demo-frontend."
echo "Only configure required reviewers on 'qa', 'prod', 'dev-infra', 'qa-infra', and 'prod-infra' — leave 'dev', 'qa-candidate', and the '-infra-plan' environments unprotected."
