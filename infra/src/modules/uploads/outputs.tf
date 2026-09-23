output "bucket_name" {
  description = "Passed to task-api as THOR_UPLOADS_BUCKET (see Thor.TaskAPI's Program.cs)"
  value       = aws_s3_bucket.thor-uploads-bucket.bucket
}

output "bucket_arn" {
  description = "Scopes task-api's task-role S3 grant to this bucket"
  value       = aws_s3_bucket.thor-uploads-bucket.arn
}
