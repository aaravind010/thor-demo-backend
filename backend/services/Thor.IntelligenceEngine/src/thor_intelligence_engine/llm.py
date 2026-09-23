from langchain_core.language_models.chat_models import BaseChatModel
from langchain_core.language_models.fake_chat_models import FakeListChatModel

from thor_intelligence_engine.config import Settings


def get_chat_model(settings: Settings) -> BaseChatModel:
    """Returns the chat model agents build their chains on top of.

    This is the seam agents are written against, so swapping "fake" for "bedrock"
    doesn't require touching agent code.
    """
    if settings.llm_provider == "fake":
        return FakeListChatModel(responses=["ok"])

    if settings.llm_provider == "bedrock":
        # Requires the Bedrock VPC PrivateLink endpoint (ADR §13, open question O-9)
        # and the `langchain-aws` package, which isn't a base dependency yet -
        # add it when this branch is actually wired up and exercised.
        from langchain_aws import ChatBedrock  # type: ignore[import-not-found]

        return ChatBedrock(model_id=settings.bedrock_model_id, region_name=settings.bedrock_region)

    raise ValueError(f"Unknown LLM_PROVIDER: {settings.llm_provider!r}")
