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
}
