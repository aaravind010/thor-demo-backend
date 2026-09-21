"""Regenerates the Python gRPC stubs for the Intelligence Engine from the shared .proto contract.

Run via: uv run python scripts/generate_grpc.py

Output is gitignored and regenerated on demand (build time / local dev setup), not checked in,
since it's fully derived from proto/ plus the grpcio-tools version pinned in pyproject.toml.
"""

from pathlib import Path

from grpc_tools import protoc

SERVICE_DIR = Path(__file__).resolve().parent.parent
REPO_ROOT = SERVICE_DIR.parent.parent.parent
PROTO_ROOT = REPO_ROOT / "proto"
PROTO_FILE = PROTO_ROOT / "thor" / "intelligence_engine" / "v1" / "intelligence_engine.proto"
# protoc emits absolute imports based on the proto's path under --proto_path (e.g.
# `from thor.intelligence_engine.v1 import intelligence_engine_pb2`), not relative
# ones - so the generated `thor/` package has to land directly on the src root to be
# importable, not nested under thor_intelligence_engine/.
OUT_DIR = SERVICE_DIR / "src"


def main() -> None:
    if not PROTO_FILE.exists():
        raise SystemExit(f"Proto file not found: {PROTO_FILE}")

    OUT_DIR.mkdir(parents=True, exist_ok=True)

    args = [
        "protoc",
        f"--proto_path={PROTO_ROOT}",
        f"--python_out={OUT_DIR}",
        f"--grpc_python_out={OUT_DIR}",
        str(PROTO_FILE),
    ]
    if protoc.main(args) != 0:
        raise SystemExit("protoc codegen failed")

    # protoc doesn't emit __init__.py for the package dirs it creates; add them so the
    # generated tree is a regular (not namespace) package, importable the same way
    # regardless of how the project is installed.
    package_dir = OUT_DIR / "thor"
    for directory in (package_dir, package_dir / "intelligence_engine", package_dir / "intelligence_engine" / "v1"):
        init_file = directory / "__init__.py"
        if not init_file.exists():
            init_file.write_text("")

    print(f"Generated gRPC stubs into {OUT_DIR}")


if __name__ == "__main__":
    main()
