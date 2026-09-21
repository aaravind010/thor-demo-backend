import grpc

from thor.intelligence_engine.v1 import intelligence_engine_pb2, intelligence_engine_pb2_grpc
from thor_intelligence_engine.agents.base import Agent


class IntelligenceEngineServicer(intelligence_engine_pb2_grpc.IntelligenceEngineServiceServicer):
    def __init__(self, agents: dict[str, Agent]) -> None:
        self._agents = agents

    async def InvokeAgent(self, request, context):
        agent = self._agents.get(request.agent_name)
        if agent is None:
            await context.abort(grpc.StatusCode.NOT_FOUND, f"Unknown agent_name: {request.agent_name!r}")

        try:
            output = agent.run(request.input)
        except Exception as exc:  # noqa: BLE001 - surfaced to the caller as an internal gRPC error
            await context.abort(grpc.StatusCode.INTERNAL, f"Agent '{request.agent_name}' failed: {exc}")

        return intelligence_engine_pb2.InvokeAgentResponse(output=output)
