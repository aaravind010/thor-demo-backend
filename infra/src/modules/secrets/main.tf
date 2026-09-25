terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

resource "aws_secretsmanager_secret" "thor-authorizer-salt" {
  name        = "thor-${var.environment}-secret-authorizer-salt"
  description = "Salt for Thor.Authorizer's PBKDF2 API-key hashing. SecretString set out-of-band."

  recovery_window_in_days = var.recovery_window_in_days

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

# RS256 keypair for connector access tokens (ADR §5.2/§8). Both halves live in one secret so a
# rotation can't leave the signer and the verifiers on different keys. SecretString set
# out-of-band as JSON: {"private_key": "-----BEGIN PRIVATE KEY-----\n...", "public_key": "..."}.
# Thor.Api reads private_key (it alone signs); Thor.TaskApi reads public_key. The authorizer
# needs the public half too but can't source it here — see var.connector_jwt_public_key in the
# root module for why.
resource "aws_secretsmanager_secret" "thor-connector-jwt" {
  name        = "thor-${var.environment}-secret-connector-jwt"
  description = "RS256 keypair Thor.Api signs connector JWTs with and Thor.TaskApi verifies against. SecretString set out-of-band."

  recovery_window_in_days = var.recovery_window_in_days

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}

resource "aws_secretsmanager_secret" "thor-api-key-pepper" {
  name        = "thor-${var.environment}-secret-api-key-pepper"
  description = "Pepper Thor.Api mixes into API-key/refresh-token hashing (ADR §5.2/§8). SecretString set out-of-band."

  recovery_window_in_days = var.recovery_window_in_days

  tags = var.tags

  # Cloud Custodian auto-tags this after creation and an SCP blocks removing it — ignore tags to avoid fighting it.
  lifecycle {
    ignore_changes = [tags, tags_all]
  }
}
