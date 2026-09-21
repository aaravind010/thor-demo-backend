from langchain_core.language_models.fake_chat_models import FakeListChatModel

from thor_intelligence_engine.agents.echo_agent import EchoAgent


def test_echo_agent_returns_fake_model_response():
    chat_model = FakeListChatModel(responses=["hello back"])
    agent = EchoAgent(chat_model)

    result = agent.run("hello")

    assert result == "hello back"
