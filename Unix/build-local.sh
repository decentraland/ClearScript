#!/bin/bash
#
# Build the native ClearScriptV8 shared library locally (intended for
# WSL / Ubuntu 22.04). Only invokes the V8/native Makefile target — the
# C# samples that CI builds are skipped, since they need .NET 9.0 SDK
# which isn't in jammy's default repos. CI builds those because the
# ubuntu-22.04 runner image ships .NET 9 preinstalled.
#
# Usage: ./Unix/build-local.sh [x64|arm64|all]    (default: x64)
#
# Installs needed prerequisites on demand (clang, make, pkgconf, python3,
# curl, build-essential). The arm64 cross-build toolchain
# (g++-10-aarch64-linux-gnu) is installed only when building for arm64.

set -Eeuo pipefail

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

require_ubuntu_2204() {
    if ! command -v apt-get >/dev/null 2>&1; then
        echo "This script targets Ubuntu 22.04 (apt). Install prerequisites manually." >&2
        exit 1
    fi
    if [[ -r /etc/os-release ]]; then
        # shellcheck disable=SC1091
        . /etc/os-release
        if [[ "${ID:-}" != "ubuntu" || "${VERSION_ID:-}" != "22.04" ]]; then
            echo "Warning: this script targets Ubuntu 22.04; detected ${PRETTY_NAME:-unknown}." >&2
            echo "         Continuing anyway — apt package names may differ." >&2
        fi
    fi
}

# Avoid interactive prompts during apt-get (notably needrestart's TUI,
# which Ubuntu 22.04 enables by default and which hangs in scripts).
export DEBIAN_FRONTEND=noninteractive
export NEEDRESTART_MODE=a
export NEEDRESTART_SUSPEND=1

apt_updated=false
apt_install() {
    # apt_install pkg1 pkg2 ...
    if [[ "$apt_updated" == false ]]; then
        sudo -E apt-get update
        apt_updated=true
    fi
    sudo -E apt-get install -y --no-install-recommends "$@"
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
        make -f Unix/ClearScriptV8/Makefile
    else
        make -f Unix/ClearScriptV8/Makefile CPU="$target_cpu"
    fi
}

require_ubuntu_2204
ensure_build_prereqs

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
