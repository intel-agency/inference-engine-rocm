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

# Ensure cmake is up to date (Ubuntu 22.04 has 3.22, ORT might want newer)
python3 -m pip install cmake --upgrade

echo ">>> [4/5] Configuring Environment..."
# Force CMake to find the ROCm compiler
export ROCM_PATH="$ROCM_HOME"
export HIP_PATH="$ROCM_HOME"
export PATH="$ROCM_HOME/bin:$PATH"

echo ">>> [5/5] Starting Compilation (This takes 30-60 mins)..."
# --skip_tests: Crucial because the build container usually cannot access the GPU hardware directly
# --cmake_extra_defines: Optimizes for common RDNA2/3 architectures (gfx1030=RX6800/6900, gfx1031=RX6700, gfx1100=RX7900)
./build.sh \
    --config Release \
    --build_wheel \
    --use_rocm \
    --rocm_home "$ROCM_HOME" \
    --skip_tests \
    --skip_submodule_sync \
    --parallel \
    --allow_running_as_root \
    --cmake_extra_defines CMAKE_HIP_ARCHITECTURES="gfx1030;gfx1031;gfx1100"

echo " SUCCESS! Copying artifacts..."
mkdir -p /code/artifacts
cp build/Linux/Release/libonnxruntime.so /code/artifacts/
cp build/Linux/Release/libonnxruntime_providers_rocm.so /code/artifacts/

echo "Artifacts copied to /code/artifacts"
