output "certificate_arns" {
  description = "Map of certificate logical name -> validated ARN"
  value       = { for k, v in aws_acm_certificate_validation.this : k => v.certificate_arn }
}
