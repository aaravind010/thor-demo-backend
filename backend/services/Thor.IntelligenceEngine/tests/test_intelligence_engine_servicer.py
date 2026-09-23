import grpc
import pytest

from thor.intelligence_engine.v1 import intelligence_engine_pb2, intelligence_engine_pb2_grpc


async def test_invoke_agent_echo_returns_response(grpc_channel):
    stub = intelligence_engine_pb2_grpc.IntelligenceEngineServiceStub(grpc_channel)

    response = await stub.InvokeAgent(
        intelligence_engine_pb2.InvokeAgentRequest(tenant_id="t1", agent_name="echo", input="hello")
    )

    assert response.output


async def test_invoke_agent_unknown_agent_returns_not_found(grpc_channel):
    stub = intelligence_engine_pb2_grpc.IntelligenceEngineServiceStub(grpc_channel)

    with pytest.raises(grpc.aio.AioRpcError) as exc_info:
        await stub.InvokeAgent(
            intelligence_engine_pb2.InvokeAgentRequest(tenant_id="t1", agent_name="nope", input="hello")
        )

    assert exc_info.value.code() == grpc.StatusCode.NOT_FOUND
