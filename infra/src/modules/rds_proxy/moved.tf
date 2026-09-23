# The thor-api consumer was keyed "thor" until 3ff2b21 renamed it. A for_each key rename reads as
# create-new + destroy-old, and the create fails with InvalidPermission.Duplicate against the rule
# the old key already owns. This re-keys the existing state entry instead.
# No-op in any state that never held the old key, so it is safe to carry into qa/prod.
moved {
  from = aws_vpc_security_group_ingress_rule.rds_proxy_security_group_ingress["thor"]
  to   = aws_vpc_security_group_ingress_rule.rds_proxy_security_group_ingress["thor-api"]
}
