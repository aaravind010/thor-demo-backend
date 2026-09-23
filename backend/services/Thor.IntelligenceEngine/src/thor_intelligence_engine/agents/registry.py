from thor_intelligence_engine.agents.base import Agent
from thor_intelligence_engine.agents.echo_agent import EchoAgent
from thor_intelligence_engine.config import Settings
from thor_intelligence_engine.llm import get_chat_model


def build_registry(settings: Settings) -> dict[str, Agent]:
    chat_model = get_chat_model(settings)
    return {
        "echo": EchoAgent(chat_model),
    }
