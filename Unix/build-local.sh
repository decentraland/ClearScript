#!/bin/bash
#
# Reproduce the Linux CI build locally (intended for WSL / Ubuntu 22.04).
# Usage: ./Unix/build-local.sh [x64|arm64|all]    (default: x64)
#
# Installs all prerequisites listed in docs/Details/Build.html
# (git, .NET 8.0 SDK, clang, make, pkgconf) plus what V8's depot_tools
# needs (python3, curl, build-essential). The arm64 cross-build
# toolchain (g++-10-aarch64-linux-gnu) is installed on demand.

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

require_apt() {
    if ! command -v apt-get >/dev/null 2>&1; then
        echo "This script targets Ubuntu/Debian (apt). Install prerequisites manually." >&2
        exit 1
    fi
}

apt_updated=false
apt_install() {
    # apt_install pkg1 pkg2 ...
    if [[ "$apt_updated" == false ]]; then
        sudo apt-get update
        apt_updated=true
    fi
    sudo apt-get install -y "$@"
}

ensure_build_prereqs() {
    # Map each required tool to the apt package that provides it.
    local -a packages=()
    command -v git      >/dev/null 2>&1 || packages+=(git)
    command -v make     >/dev/null 2>&1 || packages+=(build-essential)
    command -v clang    >/dev/null 2>&1 || packages+=(clang)
    command -v pkgconf  >/dev/null 2>&1 || packages+=(pkgconf)
    command -v python3  >/dev/null 2>&1 || packages+=(python3)
    command -v curl     >/dev/null 2>&1 || packages+=(curl)
    if (( ${#packages[@]} > 0 )); then
        echo "Installing build prerequisites: ${packages[*]} ..."
        apt_install "${packages[@]}"
    fi
}

ensure_dotnet8() {
    if command -v dotnet >/dev/null 2>&1 \
            && dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
        return
    fi
    echo "Installing .NET 8.0 SDK ..."
    apt_install dotnet-sdk-8.0
}

ensure_arm64_toolchain() {
    if ! command -v aarch64-linux-gnu-g++-10 >/dev/null 2>&1; then
        echo "Installing arm64 cross-build tools ..."
        apt_install g++-10-aarch64-linux-gnu
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

require_apt
ensure_build_prereqs
ensure_dotnet8

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
