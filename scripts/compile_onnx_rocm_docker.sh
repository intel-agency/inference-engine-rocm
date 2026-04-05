#!/bin/bash
set -e

# === CONFIGURATION ===
# We pin to v1.19.2 because 'main' is unstable and often breaks build flags
ORT_TAG="v1.19.2"
ROCM_HOME="/opt/rocm"

echo ">>> [0/5] Installing git (not present in base ROCm image)..."
apt-get update -qq
apt-get install -y --no-install-recommends git

echo ">>> [1/5] Setting up Git Safe Directory..."
git config --global --add safe.directory /code

# Setup workspace inside the container
# We use a subdirectory so we don't clutter the root mapped volume too much
mkdir -p /code/external_build_work
cd /code/external_build_work

echo ">>> [2/5] Checking out stable release $ORT_TAG..."
if [ ! -d "onnxruntime" ]; then
    git clone --recursive -b $ORT_TAG https://github.com/microsoft/onnxruntime.git
fi
cd onnxruntime

echo ">>> [3/5] Installing Build Dependencies..."
# The base image is barebones; we need cmake and python tools
apt-get update
apt-get install -y build-essential cmake git python3 python3-dev python3-pip libprotobuf-dev protobuf-compiler
# ROCm math libraries required by ORT ROCm provider
apt-get install -y hiprand rocblas miopen-hip 2>/dev/null || \
    apt-get install -y hiprand-dev rocblas-dev 2>/dev/null || true

# Ensure cmake is up to date but below 4.0 (cmake 4.x breaks ORT dependency cmake_minimum_required)
python3 -m pip install 'cmake>=3.26,<4.0' --upgrade

echo ">>> [4/5] Configuring Environment..."
# Force CMake to find the ROCm compiler
export ROCM_PATH="$ROCM_HOME"
export HIP_PATH="$ROCM_HOME"
export PATH="$ROCM_HOME/bin:$PATH"

# Detect ROCm version - try multiple sources in order of reliability
ROCM_MAJOR="" ; ROCM_MINOR="" ; ROCM_PATCH=""

# Source 1: existing header (may be in wrong format for ORT cmake - we fix it below)
if [ -f "$ROCM_HOME/include/rocm_version.h" ]; then
    ROCM_MAJOR=$(grep 'define ROCM_VERSION_MAJOR' "$ROCM_HOME/include/rocm_version.h" | awk '{print $NF}')
    ROCM_MINOR=$(grep 'define ROCM_VERSION_MINOR' "$ROCM_HOME/include/rocm_version.h" | awk '{print $NF}')
    ROCM_PATCH=$(grep 'define ROCM_VERSION_PATCH'  "$ROCM_HOME/include/rocm_version.h" | awk '{print $NF}')
fi

# Source 2: HIP cmake config
if [ -z "$ROCM_MAJOR" ]; then
    _HIP_VER=$(grep 'PACKAGE_VERSION' /opt/rocm/lib/cmake/hip/hip-config-version.cmake 2>/dev/null | head -1 | grep -oP '[\d.]+' | head -1)
    ROCM_MAJOR=$(echo "$_HIP_VER" | cut -d. -f1)
    ROCM_MINOR=$(echo "$_HIP_VER" | cut -d. -f2)
    ROCM_PATCH=$(echo "$_HIP_VER" | cut -d. -f3)
fi

# Source 3: dpkg ROCm-specific packages only
if [ -z "$ROCM_MAJOR" ]; then
    _PKG_VER=$(dpkg -l 2>/dev/null | grep -E '\brocm-dev\b|\brocm-core\b|\bhip-runtime-amd\b' | awk '{print $3}' | grep -oP '^\d+\.\d+\.\d+' | sort -V | tail -1)
    ROCM_MAJOR=$(echo "$_PKG_VER" | cut -d. -f1)
    ROCM_MINOR=$(echo "$_PKG_VER" | cut -d. -f2)
    ROCM_PATCH=$(echo "$_PKG_VER" | cut -d. -f3)
fi

# Source 4: .info/version text file
if [ -z "$ROCM_MAJOR" ]; then
    _INFO_VER=$(cat /opt/rocm/.info/version 2>/dev/null | tr -d '[:space:]')
    ROCM_MAJOR=$(echo "$_INFO_VER" | cut -d. -f1)
    ROCM_MINOR=$(echo "$_INFO_VER" | cut -d. -f2)
    ROCM_PATCH=$(echo "$_INFO_VER" | cut -d. -f3)
fi

ROCM_VERSION_STRING="${ROCM_MAJOR}.${ROCM_MINOR}.${ROCM_PATCH}"
echo ">>> Detected ROCm version: $ROCM_VERSION_STRING"

# ALWAYS rewrite rocm_version.h in the exact format ORT v1.19.2 cmake expects.
# Newer ROCm images ship this header in a different format that breaks ORT cmake.
echo ">>> Writing $ROCM_HOME/include/rocm_version.h (ORT-compatible format)"
mkdir -p "$ROCM_HOME/include"
cat > "$ROCM_HOME/include/rocm_version.h" << ROCM_VER_EOF
#pragma once
#define ROCM_VERSION_MAJOR ${ROCM_MAJOR}
#define ROCM_VERSION_MINOR ${ROCM_MINOR}
#define ROCM_VERSION_PATCH ${ROCM_PATCH}
ROCM_VER_EOF

echo ">>> [5/5] Starting Compilation (This takes 30-60 mins)..."
# --skip_tests: Crucial because the build container usually cannot access the GPU hardware directly
# --cmake_extra_defines: Optimizes for common RDNA2/3 architectures (gfx1030=RX6800/6900, gfx1031=RX6700, gfx1100=RX7900)

# Pre-fetch Eigen via git to avoid GitLab tarball hash instability (GitLab regenerates
# archives periodically, changing hashes and breaking ORT's FetchContent verify step).
EIGEN_SRC_DIR="/code/external_build_work/eigen-src"
if [ ! -d "$EIGEN_SRC_DIR/.git" ]; then
    echo ">>> Pre-fetching Eigen 3.4.0 via git (bypasses archive hash verification)..."
    git clone --depth=1 --branch 3.4.0 https://gitlab.com/libeigen/eigen.git "$EIGEN_SRC_DIR"
fi
./build.sh \
    --config Release \
    --build_wheel \
    --use_rocm \
    --rocm_home "$ROCM_HOME" \
    --rocm_version "$ROCM_VERSION_STRING" \
    --skip_tests \
    --skip_submodule_sync \
    --parallel \
    --allow_running_as_root \
    --cmake_extra_defines CMAKE_HIP_ARCHITECTURES="gfx1030;gfx1031;gfx1100" \
    --cmake_extra_defines FETCHCONTENT_SOURCE_DIR_EIGEN="$EIGEN_SRC_DIR"

echo " SUCCESS! Copying artifacts..."
mkdir -p /code/artifacts
cp build/Linux/Release/libonnxruntime.so /code/artifacts/
cp build/Linux/Release/libonnxruntime_providers_rocm.so /code/artifacts/

echo "Artifacts copied to /code/artifacts"
