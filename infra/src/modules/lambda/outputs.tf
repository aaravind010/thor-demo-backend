output "lambda_function_arn" {
  value = aws_lambda_function.thor-authorizer-lambda.arn
}

output "lambda_function_name" {
  value = aws_lambda_function.thor-authorizer-lambda.function_name
}

output "lambda_invoke_arn" {
  description = "Used as the API Gateway authorizer's authorizer_uri"
  value       = aws_lambda_function.thor-authorizer-lambda.invoke_arn
}
