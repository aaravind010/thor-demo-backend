terraform {
  required_version = ">= 1.15"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

data "aws_caller_identity" "current" {}

locals {
  name_prefix = "thor-${var.environment}-frontend"
  # Globally unique — bucket names share a namespace across all of AWS, not just this account.
  bucket_name = "thor-frontend-${var.environment}-${data.aws_caller_identity.current.account_id}"
}

# Private bucket — no website hosting, no public ACLs. CloudFront reaches it through the origin access control below, never directly.
resource "aws_s3_bucket" "thor-fe-bucket" {
  bucket = local.bucket_name

  tags = merge(var.tags, {
    Name = local.bucket_name
  })
}

resource "aws_s3_bucket_public_access_block" "thor-fe-bucket-pab" {
  bucket = aws_s3_bucket.thor-fe-bucket.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# BucketOwnerEnforced — object ACLs are disabled entirely, matching an OAC-fronted bucket where only the bucket policy (not ACLs) grants access.
resource "aws_s3_bucket_ownership_controls" "thor-fe-bucket-ownership" {
  bucket = aws_s3_bucket.thor-fe-bucket.id

  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_cloudfront_origin_access_control" "thor-fe-oac" {
  name                              = local.name_prefix
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

resource "aws_cloudfront_distribution" "thor-fe-cdn" {
  enabled             = true
  comment             = local.name_prefix
  default_root_object = "index.html"
  price_class         = var.price_class

  origin {
    domain_name              = aws_s3_bucket.thor-fe-bucket.bucket_regional_domain_name
    origin_id                = local.bucket_name
    origin_access_control_id = aws_cloudfront_origin_access_control.thor-fe-oac.id
  }

  default_cache_behavior {
    target_origin_id       = local.bucket_name
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods        = ["GET", "HEAD"]
    cached_methods         = ["GET", "HEAD"]
    compress               = true

    # AWS-managed "CachingOptimized" policy — long-lived caching for a static SPA build, no per-behavior TTL tuning needed here.
    cache_policy_id = "658327ea-f89d-4fab-a63d-7e88639e58f6"
  }

  # SPA client-side routing — any path that isn't a real object in the bucket (a React Router route, for example) falls back to index.html instead of surfacing S3's 403/404 to the browser.
  custom_error_response {
    error_code         = 403
    response_code      = 200
    response_page_path = "/index.html"
  }

  custom_error_response {
    error_code         = 404
    response_code      = 200
    response_page_path = "/index.html"
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  # Default *.cloudfront.net certificate — no custom domain wired in yet.
  viewer_certificate {
    cloudfront_default_certificate = true
  }

  tags = merge(var.tags, {
    Name = local.name_prefix
  })
}

data "aws_iam_policy_document" "cloudfront_access" {
  statement {
    sid       = "AllowCloudFrontServicePrincipalReadOnly"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.thor-fe-bucket.arn}/*"]

    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.thor-fe-cdn.arn]
    }
  }
}

resource "aws_s3_bucket_policy" "thor-fe-bucket-policy" {
  bucket = aws_s3_bucket.thor-fe-bucket.id
  policy = data.aws_iam_policy_document.cloudfront_access.json
}
