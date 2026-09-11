output "authorizer_salt_secret_arn" {
  description = "Passed to the lambda module as THOR_AUTHORIZER_SALT_SECRET_ID and used to scope its secretsmanager:GetSecretValue IAM permission"
  value       = aws_secretsmanager_secret.thor-authorizer-salt.arn
}
