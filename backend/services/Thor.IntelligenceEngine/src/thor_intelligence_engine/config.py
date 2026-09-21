from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    grpc_port: int = 8443
    # Matches the env var names infra bakes into the container for this service
    # (infra/src/modules/ecs/services.tf). Unset in local dev -> insecure server.
    tls_cert_path: str | None = None
    tls_cert_key_path: str | None = None

    # "fake" (default, no AWS needed) or "bedrock" (needs the Bedrock PrivateLink
    # endpoint, ADR §13 open question O-9 — see thor_intelligence_engine.llm).
    llm_provider: str = "fake"
    bedrock_region: str = "us-east-1"
    bedrock_model_id: str = "anthropic.claude-3-haiku-20240307-v1:0"
