# One repo per service, per environment; unconditional, unlike everything else here, since a repo must exist before enable_compute can be turned on.
resource "aws_ecr_repository" "thor-ecr-repo" {
  for_each = var.services

  name                 = "${each.key}-${var.environment}"
  image_tag_mutability = "IMMUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }

  encryption_configuration {
    encryption_type = "AES256"
  }

  tags = var.tags
}

resource "aws_ecr_lifecycle_policy" "thor-ecr-lifecycle" {
  for_each = var.services

  repository = aws_ecr_repository.thor-ecr-repo[each.key].name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Expire untagged images after 7 days"
        selection = {
          tagStatus   = "untagged"
          countType   = "sinceImagePushed"
          countUnit   = "days"
          countNumber = 7
        }
        action = { type = "expire" }
      },
      {
        rulePriority = 2
        description  = "Keep only the last 20 tagged images"
        selection = {
          tagStatus      = "tagged"
          tagPatternList = ["*"]
          countType      = "imageCountMoreThan"
          countNumber    = 20
        }
        action = { type = "expire" }
      }
    ]
  })
}

# Lets a downstream environment's deploy role pull-and-retag from this environment's repos during promotion (e.g. qa's deploy role reading dev's repos). Same-account promotion doesn't need this (an IAM policy on the puller's role is enough) — this is only for a cross-account puller (qa -> prod, once the prod account/role are real), which needs the resource owner's side to grant it too.
resource "aws_ecr_repository_policy" "cross_account_pull" {
  for_each = length(var.cross_account_pull_principal_arns) > 0 ? var.services : {}

  repository = aws_ecr_repository.thor-ecr-repo[each.key].name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid       = "CrossAccountPull"
      Effect    = "Allow"
      Principal = { AWS = var.cross_account_pull_principal_arns }
      Action = [
        "ecr:GetDownloadUrlForLayer",
        "ecr:BatchGetImage",
        "ecr:BatchCheckLayerAvailability",
      ]
    }]
  })
} 
