#!/bin/bash
#
# Reproduce the Linux CI build locally (intended for WSL / Ubuntu 22.04).
# Usage: ./Unix/build-local.sh [x64|arm64|all]    (default: x64)
#
# Prerequisites (same as docs/Details/Build.html):
#   git, .NET 8.0 SDK, clang, make, pkgconf
# arm64 cross-build tooling (g++-10-aarch64-linux-gnu) is installed
# automatically on demand.

set -euo pipefail

cpu="${1:-x64}"

if [[ "$cpu" != "x64" && "$cpu" != "arm64" && "$cpu" != "all" ]]; then
    echo "Usage: $0 [x64|arm64|all]" >&2
    exit 2
fi

# Run from repo root regardless of invocation directory.
cd "$(dirname "$0")/.."

dump_logs() {
    echo
    echo "===== V8 build logs ====="
    local found=false
    for f in V8/build/v8/apply-cherry-picks.log \
             V8/build/v8/apply-patch.log \
             V8/build/v8/gn-x64-Release.log \
             V8/build/v8/build-x64-Release.log \
             V8/build/v8/gn-arm64-Release.log \
             V8/build/v8/build-arm64-Release.log; do
        if [[ -f "$f" ]]; then
            echo "===== $f ====="
            cat "$f"
            found=true
        fi
    done
    if [[ "$found" == false ]]; then
        echo "(no V8 build logs found under V8/build/v8/)"
    fi
}

trap dump_logs ERR

ensure_arm64_toolchain() {
    if ! command -v aarch64-linux-gnu-g++-10 >/dev/null 2>&1; then
        echo "Installing arm64 cross-build tools (sudo apt-get) ..."
        sudo apt-get update
        sudo apt-get install -y g++-10-aarch64-linux-gnu
    fi
}

build_one() {
    local target_cpu="$1"
    echo "==== Building for CPU=$target_cpu ===="
    if [[ "$target_cpu" == "x64" ]]; then
        make -f Unix/Makefile
    else
        make -f Unix/Makefile CPU="$target_cpu"
    fi
}

case "$cpu" in
    x64)
        build_one x64
        ;;
    arm64)
        ensure_arm64_toolchain
        build_one arm64
        ;;
    all)
        build_one x64
        ensure_arm64_toolchain
        build_one arm64
        ;;
esac

echo "Done."
