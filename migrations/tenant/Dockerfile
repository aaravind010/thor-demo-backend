# Tenant migration runner: Atlas CLI + psql + AWS CLI. Step Functions runs it on Fargate
# (ecs:runTask.sync) with THOR_MODE=inspect|apply and the execution input in THOR_INPUT.
# Single container, no dev database: it only inspects and applies — CI does the diffing.
# Build context is this directory (migrations/tenant).
ARG ATLAS_VERSION=1.3.0
FROM arigaio/atlas:${ATLAS_VERSION}-alpine

RUN apk add --no-cache postgresql16-client aws-cli jq \
    && (command -v atlas || ln -s /atlas /usr/local/bin/atlas) \
    && adduser -D -u 10001 runner

WORKDIR /app
COPY entrypoint.sh lib.sh inspect.sh apply.sh /app/
RUN chmod 0555 /app/*.sh

USER runner
ENTRYPOINT ["/app/entrypoint.sh"]
