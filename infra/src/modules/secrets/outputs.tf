output "authorizer_salt_secret_arn" {
  description = "Passed to the lambda module as THOR_AUTHORIZER_SALT_SECRET_ID and used to scope its secretsmanager:GetSecretValue IAM permission"
  value       = aws_secretsmanager_secret.thor-authorizer-salt.arn
}

output "connector_jwt_secret_arn" {
  description = "Bare ARN of the connector JWT keypair secret. Each service's container definition appends a \":<json-key>::\" suffix to select its half; the execution role's GetSecretValue grant uses this bare form, since the suffixed form matches nothing as an IAM resource."
  value       = aws_secretsmanager_secret.thor-connector-jwt.arn
}

output "api_key_pepper_secret_arn" {
  description = "Bare ARN of the API-key pepper secret, consumed whole (no json-key suffix) as THOR_API_KEY_PEPPER by thor-api"
  value       = aws_secretsmanager_secret.thor-api-key-pepper.arn
}
