from langchain_core.language_models.chat_models import BaseChatModel
from langchain_core.output_parsers import StrOutputParser
from langchain_core.prompts import ChatPromptTemplate


class EchoAgent:
    """Minimal illustrative agent: a single LCEL chain (prompt | chat model | parser).

    Proves the LangChain wiring pattern new agents should follow; not real business logic.
    """

    def __init__(self, chat_model: BaseChatModel) -> None:
        prompt = ChatPromptTemplate.from_messages(
            [
                ("system", "You are the Thor Intelligence Engine base agent. Echo the user's input verbatim."),
                ("human", "{input}"),
            ]
        )
        self._chain = prompt | chat_model | StrOutputParser()

    def run(self, input: str) -> str:
        return self._chain.invoke({"input": input})
