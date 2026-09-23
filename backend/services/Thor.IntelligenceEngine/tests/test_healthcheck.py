import asyncio

import grpc
import pytest_asyncio
from grpc_health.v1 import health_pb2, health_pb2_grpc
from grpc_health.v1._async import HealthServicer

from thor_intelligence_engine.healthcheck import check


@pytest_asyncio.fixture
async def health_server():
    server = grpc.aio.server()
    health_servicer = HealthServicer()
    health_pb2_grpc.add_HealthServicer_to_server(health_servicer, server)
    await health_servicer.set("", health_pb2.HealthCheckResponse.SERVING)
    port = server.add_insecure_port("localhost:0")
    await server.start()
    try:
        yield port
    finally:
        await server.stop(None)


async def test_check_returns_true_when_serving(health_server):
    # check() is a sync (blocking) grpc client call; run it off the event loop thread so it
    # doesn't block the loop that the grpc.aio health server needs to dispatch the RPC on.
    result = await asyncio.to_thread(check, f"localhost:{health_server}", None)
    assert result is True


async def test_check_returns_false_when_nothing_listening():
    result = await asyncio.to_thread(check, "localhost:1", None, timeout=1)
    assert result is False
