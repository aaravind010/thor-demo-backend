#!/usr/bin/env bash
# Creates the GitHub Actions OIDC identity provider in the current AWS
# account, idempotently. Run this once per AWS account before any
# create-shared-role.sh call in that account — dev, qa, and (for now) prod
# all share one account, so in practice this only needs running once.
#
# This is intentionally a script, not Terraform: GitHub Actions needs this
# role to exist before it can authenticate to AWS at all, so it can't depend
# on a Terraform apply that only GitHub Actions itself would run.
#
# Usage: ./create-github-oidc-provider.sh [aws_profile]

set -euo pipefail

export AWS_PROFILE="${1:-${AWS_PROFILE:-default}}"
OIDC_HOST="token.actions.githubusercontent.com"

ACCOUNT_ID="$(aws sts get-caller-identity --query Account --output text)"
PROVIDER_ARN="arn:aws:iam::${ACCOUNT_ID}:oidc-provider/${OIDC_HOST}"

echo "Using AWS profile '${AWS_PROFILE}' -> account ${ACCOUNT_ID}."

if aws iam get-open-id-connect-provider --open-id-connect-provider-arn "${PROVIDER_ARN}" >/dev/null 2>&1; then
  echo "OIDC provider already exists: ${PROVIDER_ARN}"
  exit 0
fi

read -rp "Create GitHub OIDC provider in account ${ACCOUNT_ID}? [y/N] " CONFIRM
[[ "${CONFIRM}" =~ ^[Yy]$ ]] || { echo "Aborted."; exit 1; }

# Fetched live rather than hardcoded — GitHub rotates this certificate, and a
# stale hardcoded thumbprint is a well-known way this silently breaks later.
THUMBPRINT="$(
  echo | openssl s_client -servername "${OIDC_HOST}" -showcerts -connect "${OIDC_HOST}:443" 2>/dev/null \
    | openssl x509 -fingerprint -sha1 -noout \
    | sed 's/.*=//' | tr -d ':' | tr 'A-F' 'a-f'
)"

aws iam create-open-id-connect-provider \
  --url "https://${OIDC_HOST}" \
  --client-id-list "sts.amazonaws.com" \
  --thumbprint-list "${THUMBPRINT}"

echo "Created: ${PROVIDER_ARN}"
