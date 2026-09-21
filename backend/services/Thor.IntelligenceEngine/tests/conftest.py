import grpc
import pytest_asyncio

from thor.intelligence_engine.v1 import intelligence_engine_pb2_grpc
from thor_intelligence_engine.agents.registry import build_registry
from thor_intelligence_engine.config import Settings
from thor_intelligence_engine.servicers.intelligence_engine_servicer import IntelligenceEngineServicer


@pytest_asyncio.fixture
async def grpc_channel():
    settings = Settings(llm_provider="fake")
    server = grpc.aio.server()
    intelligence_engine_pb2_grpc.add_IntelligenceEngineServiceServicer_to_server(
        IntelligenceEngineServicer(build_registry(settings)), server
    )
    port = server.add_insecure_port("localhost:0")
    await server.start()

    channel = grpc.aio.insecure_channel(f"localhost:{port}")
    try:
        yield channel
    finally:
        await channel.close()
        await server.stop(None)
