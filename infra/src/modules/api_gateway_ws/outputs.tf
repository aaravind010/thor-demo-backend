output "api_id" {
  value = aws_apigatewayv2_api.this.id
}

output "stage_name" {
  value = aws_apigatewayv2_stage.this.name
}

output "management_endpoint" {
  description = "@connections management API base URL — Thor.Api's THOR_WEBSOCKET_MANAGEMENT_ENDPOINT"
  value       = "https://${aws_apigatewayv2_api.this.id}.execute-api.${data.aws_region.current.region}.amazonaws.com/${aws_apigatewayv2_stage.this.name}"
}

output "connections_arn" {
  description = "Resource ARN for execute-api:ManageConnections (PostToConnection) on this stage's connections"
  value       = "${aws_apigatewayv2_api.this.execution_arn}/${aws_apigatewayv2_stage.this.name}/POST/@connections/*"
}
