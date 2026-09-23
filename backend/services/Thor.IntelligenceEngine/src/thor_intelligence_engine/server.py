import asyncio
import logging

import grpc
from grpc_health.v1 import health_pb2, health_pb2_grpc
from grpc_health.v1._async import HealthServicer  # asyncio-compatible variant, for grpc.aio servers

from thor.intelligence_engine.v1 import intelligence_engine_pb2_grpc
from thor_intelligence_engine.agents.registry import build_registry
from thor_intelligence_engine.config import Settings
from thor_intelligence_engine.servicers.intelligence_engine_servicer import IntelligenceEngineServicer

logger = logging.getLogger(__name__)


async def serve(settings: Settings) -> None:
    server = grpc.aio.server()

    intelligence_engine_pb2_grpc.add_IntelligenceEngineServiceServicer_to_server(
        IntelligenceEngineServicer(build_registry(settings)), server
    )

    health_servicer = HealthServicer()
    health_pb2_grpc.add_HealthServicer_to_server(health_servicer, server)
    await health_servicer.set("", health_pb2.HealthCheckResponse.SERVING)

    address = f"[::]:{settings.grpc_port}"
    if settings.tls_cert_path and settings.tls_cert_key_path:
        with open(settings.tls_cert_key_path, "rb") as f:
            private_key = f.read()
        with open(settings.tls_cert_path, "rb") as f:
            certificate_chain = f.read()
        credentials = grpc.ssl_server_credentials([(private_key, certificate_chain)])
        server.add_secure_port(address, credentials)
        logger.info("Starting Intelligence Engine gRPC server on %s (TLS)", address)
    else:
        server.add_insecure_port(address)
        logger.info("Starting Intelligence Engine gRPC server on %s (insecure, dev only)", address)

    await server.start()
    await server.wait_for_termination()


def run() -> None:
    logging.basicConfig(level=logging.INFO)
    asyncio.run(serve(Settings()))
