import sys

import grpc
from grpc_health.v1 import health_pb2, health_pb2_grpc

from thor_intelligence_engine.config import Settings

# Matches the self-signed cert's CN baked into the image (Dockerfile's openssl step). The
# server is always reached at "localhost" in-container, which won't match that CN, so the
# TLS handshake needs to be told what name to expect instead of what it actually dialed.
_TLS_SERVER_NAME = "intelligence-engine.internal"


def check(
    address: str, tls_root_cert: bytes | None, tls_server_name: str = _TLS_SERVER_NAME, timeout: float = 5
) -> bool:
    """Calls the standard grpc.health.v1.Health/Check RPC; True iff the server reports SERVING."""
    if tls_root_cert:
        credentials = grpc.ssl_channel_credentials(root_certificates=tls_root_cert)
        channel = grpc.secure_channel(
            address, credentials, options=(("grpc.ssl_target_name_override", tls_server_name),)
        )
    else:
        channel = grpc.insecure_channel(address)
    try:
        stub = health_pb2_grpc.HealthStub(channel)
        response = stub.Check(health_pb2.HealthCheckRequest(service=""), timeout=timeout)
        return response.status == health_pb2.HealthCheckResponse.SERVING
    except grpc.RpcError:
        return False
    finally:
        channel.close()


def run() -> int:
    settings = Settings()
    address = f"localhost:{settings.grpc_port}"

    tls_root_cert = None
    if settings.tls_cert_path and settings.tls_cert_key_path:
        with open(settings.tls_cert_path, "rb") as f:
            tls_root_cert = f.read()

    return 0 if check(address, tls_root_cert) else 1


if __name__ == "__main__":
    sys.exit(run())
