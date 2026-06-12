# Plan: CI Build Optimization for ROCm Native Libraries

**Date:** 2026-06-11
**Status:** Draft
**Current build time:** ~45 minutes
**Target build time:** ~5 min (cached) / ~15–25 min (cold)

---

## 1. Problem Statement

The `build-rocm` job in `build-rocm-linux.yml` takes ~45 minutes per run. This is because every invocation:

1. Pulls a ~5 GB ROCm Docker base image
2. Installs 20+ apt packages (build-essential, cmake, protobuf, ROCm dev headers)
3. Recursively clones the full ONNX Runtime v1.24.1 source tree (~2 GB with submodules)
4. Clones the Eigen mirror repository
5. Compiles ORT from scratch on a 2-vCPU `ubuntu-latest` runner for 5 GPU architectures

The actual ORT compilation dominates (~25–35 min), but the setup overhead adds 15–20 min of wasted time on every run.

## 2. Current Time Breakdown

| Phase | Est. Time | Addressable? |
|---|---|---|
| Docker pull (`rocm/dev-ubuntu-22.04:7.2.1`) | 2–3 min | Phase 1 (custom image) |
| `apt-get install` (build deps + ROCm dev pkgs) | 5–8 min | Phase 1 (custom image) |
| `git clone --recursive` ORT v1.24.1 | 5–10 min | Phase 1 (custom image) |
| Eigen clone + checkout | 1–2 min | Phase 1 (custom image) |
| CMake configure | 2–3 min | Phase 1 (build cache) |
| ORT compilation (`--parallel` on 2 cores, 5 GPU archs) | 25–35 min | Phase 1 (build cache) + Phase 2 (fewer archs) |
| Artifact copy / upload | 1 min | No |

## 3. Optimization Strategy

### Phase 1: Custom Docker Image + Build Caching

Eliminates the 15–20 min setup overhead and caches compiled artifacts across runs. This is the highest-impact change — warm builds drop to ~5 min.

### Phase 2: Reduce GPU Architectures

Cuts cold-build compilation time by targeting fewer AMD GPU architectures. HIP kernel compilation scales linearly with the number of target architectures.

---

## 4. Phase 1: Custom Docker Image + Build Caching

### 4.1 Custom Docker Image

**Goal:** Bake all build dependencies and the ORT source tree into a pre-built image hosted on GHCR.

#### 4.1.1 Create `Dockerfile.rocm-build`

Place at repo root:

```dockerfile
FROM rocm/dev-ubuntu-22.04:7.2.1

ENV DEBIAN_FRONTEND=noninteractive

# Build toolchain
RUN apt-get update -qq && \
    apt-get install -y --no-install-recommends \
        build-essential git cmake python3 python3-dev python3-pip \
        libprotobuf-dev protobuf-compiler && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

# ROCm dev packages (ORT v1.24.1 MIGraphX EP dependencies)
RUN apt-get update -qq && \
    apt-get install -y --no-install-recommends \
        hiprand-dev rocrand-dev rocblas-dev miopen-hip-dev hipfft-dev \
        hipsparse-dev rccl-dev rocsparse-dev roctracer-dev hipblaslt-dev \
        hipcub-dev rocprim-dev migraphx-dev \
        2>/dev/null || \
    apt-get install -y --no-install-recommends \
        hiprand rocrand rocblas miopen-hip hipfft hipsparse rccl roctracer \
        rocm-smi-lib hipblaslt hipcub rocprim migraphx \
        2>/dev/null || true

RUN apt-get install -y --no-install-recommends rocthrust-dev 2>/dev/null \
    || apt-get install -y --no-install-recommends rocthrust 2>/dev/null \
    || true

# CMake (pinned below 4.0 — ORT dependency cmake_minimum_required breaks on 4.x)
RUN python3 -m pip install --no-cache-dir 'cmake>=3.26,<4.0'

# Pre-clone ORT v1.24.1 source with submodules
RUN git clone --recursive -b v1.24.1 \
    https://github.com/microsoft/onnxruntime.git /opt/onnxruntime

# Pre-fetch Eigen (pinned to ORT v1.24.1's deps.txt commit)
RUN git clone https://github.com/eigen-mirror/eigen.git /opt/eigen-src && \
    git -C /opt/eigen-src checkout 1d8b82b0740839c0de7f1242a3585e3390ff5f33

# Environment variables for HIP/ROCm compiler discovery
ENV ROCM_PATH=/opt/rocm
ENV HIP_PATH=/opt/rocm
ENV PATH="/opt/rocm/bin:${PATH}"
```

#### 4.1.2 Create image build workflow `.github/workflows/build-docker-image.yml`

```yaml
name: Build & Push ROCm Build Image

on:
  workflow_dispatch:
  push:
    branches: [development]
    paths:
      - "Dockerfile.rocm-build"

permissions:
  contents: read
  packages: write

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}/rocm-build

jobs:
  build-image:
    name: Build & Push Docker Image
    runs-on: ubuntu-latest

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Free Disk Space
        run: |
          sudo rm -rf /usr/share/dotnet
          sudo rm -rf /opt/ghc
          sudo rm -rf "/usr/local/share/boost"
          sudo rm -rf "$AGENT_TOOLSDIRECTORY"
          sudo docker image prune --all --force

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract metadata
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=raw,value=latest
            type=sha,prefix=

      - name: Build and push
        uses: docker/build-push-action@v6
        with:
          context: .
          file: ./Dockerfile.rocm-build
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
```

#### 4.1.3 Update `compile_onnx_rocm_docker.sh`

The script becomes significantly simpler since dependencies and source are pre-baked:

```bash
#!/bin/bash
set -e

ORT_TAG="v1.24.1"
ROCM_HOME="/opt/rocm"

echo ">>> [1/3] Setting up workspace..."
git config --global --add safe.directory /code
mkdir -p /code/external_build_work
cd /code/external_build_work

echo ">>> [2/3] Linking pre-baked ORT source..."
if [ ! -d "onnxruntime" ]; then
    cp -a /opt/onnxruntime onnxruntime
fi
cd onnxruntime

echo ">>> [3/3] Starting Compilation..."
# Rewrite rocm_version.h (ORT cmake expects a specific format)
ROCM_MAJOR="" ; ROCM_MINOR="" ; ROCM_PATCH=""
if [ -f "$ROCM_HOME/include/rocm_version.h" ]; then
    ROCM_MAJOR=$(grep 'define ROCM_VERSION_MAJOR' "$ROCM_HOME/include/rocm_version.h" | awk '{print $NF}')
    ROCM_MINOR=$(grep 'define ROCM_VERSION_MINOR' "$ROCM_HOME/include/rocm_version.h" | awk '{print $NF}')
    ROCM_PATCH=$(grep 'define ROCM_VERSION_PATCH'  "$ROCM_HOME/include/rocm_version.h" | awk '{print $NF}')
fi
if [ -z "$ROCM_MAJOR" ] || [ -z "$ROCM_MINOR" ] || [ -z "$ROCM_PATCH" ]; then
    echo "ERROR: Failed to detect ROCm version." >&2
    exit 1
fi
mkdir -p "$ROCM_HOME/include"
cat > "$ROCM_HOME/include/rocm_version.h" << ROCM_VER_EOF
#pragma once
#define ROCM_VERSION_MAJOR ${ROCM_MAJOR}
#define ROCM_VERSION_MINOR ${ROCM_MINOR}
#define ROCM_VERSION_PATCH ${ROCM_PATCH}
ROCM_VER_EOF

# GPU architecture targets — overridable via env var (see Phase 2)
HIP_ARCHS="${HIP_ARCHITECTURES:-gfx1030;gfx1031;gfx1100;gfx1101;gfx1102}"

./build.sh \
    --config Release \
    --build_shared_lib \
    --use_migraphx \
    --migraphx_home "$ROCM_HOME" \
    --rocm_home "$ROCM_HOME" \
    --skip_tests \
    --skip_submodule_sync \
    --parallel \
    --allow_running_as_root \
    --cmake_extra_defines CMAKE_HIP_ARCHITECTURES="$HIP_ARCHS" \
    --cmake_extra_defines FETCHCONTENT_SOURCE_DIR_EIGEN="/opt/eigen-src"

echo " SUCCESS! Copying artifacts..."
mkdir -p /code/artifacts
cp build/Linux/Release/libonnxruntime.so /code/artifacts/
cp build/Linux/Release/libonnxruntime_providers_shared.so /code/artifacts/
cp build/Linux/Release/libonnxruntime_providers_migraphx.so /code/artifacts/
echo "Artifacts copied to /code/artifacts"
```

#### 4.1.4 Update `build-rocm-linux.yml` — `build-rocm` job

Replace the base image reference and remove the dependency install steps:

```yaml
  build-rocm:
    name: Build ROCm Native Libs
    runs-on: ubuntu-latest

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Free Disk Space
        run: |
          sudo rm -rf /usr/share/dotnet
          sudo rm -rf /opt/ghc
          sudo rm -rf "/usr/local/share/boost"
          sudo rm -rf "$AGENT_TOOLSDIRECTORY"
          sudo docker image prune --all --force

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Set script permissions
        run: chmod +x scripts/compile_onnx_rocm_docker.sh

      - name: Compile Native ROCm Libs (Docker)
        run: |
          mkdir -p artifacts
          docker run --rm \
            -v ${{ github.workspace }}:/code \
            -w /code \
            ghcr.io/intel-agency/inference-engine-rocm/rocm-build:latest \
            /code/scripts/compile_onnx_rocm_docker.sh

      - name: Upload Native Artifacts
        uses: actions/upload-artifact@v4
        with:
          name: native-rocm-libs
          path: artifacts/*.so
          retention-days: 5
```

### 4.2 Build Directory Caching

**Goal:** Cache the compiled `.so` artifacts so unchanged rebuilds skip compilation entirely.

#### 4.2.1 Add cache step to `build-rocm` job

Insert before the Docker compile step. The cache key is derived from the compile script hash — if the script hasn't changed, the cached `.so` files are restored:

```yaml
      - name: Restore ORT build cache
        uses: actions/cache@v4
        id: ort-cache
        with:
          path: build-cache
          key: ort-rocm-${{ hashFiles('scripts/compile_onnx_rocm_docker.sh', 'Dockerfile.rocm-build') }}

      - name: Compile Native ROCm Libs (Docker)
        if: steps.ort-cache.outputs.cache-hit != 'true'
        run: |
          mkdir -p artifacts
          docker run --rm \
            -v ${{ github.workspace }}:/code \
            -w /code \
            ghcr.io/intel-agency/inference-engine-rocm/rocm-build:latest \
            /code/scripts/compile_onnx_rocm_docker.sh

      - name: Restore cached artifacts
        if: steps.ort-cache.outputs.cache-hit == 'true'
        run: |
          mkdir -p artifacts
          cp build-cache/*.so artifacts/

      - name: Save ORT build cache
        if: steps.ort-cache.outputs.cache-hit != 'true'
        run: |
          mkdir -p build-cache
          cp artifacts/*.so build-cache/
```

**Important caveat:** GitHub Actions cache has a 10 GB per-repo limit and `.so` files are ~200–300 MB total. This fits comfortably, but monitor cache usage if multiple branches are cached.

#### 4.2.2 Cache key strategy

| Component | Included in key | Rationale |
|---|---|---|
| `scripts/compile_onnx_rocm_docker.sh` | Yes | Script changes mean build flags or ORT version changed |
| `Dockerfile.rocm-build` | Yes | Image changes mean different deps or source versions |
| Branch name | No | Same script should produce same artifacts regardless of branch |
| `github.run_number` | No | Would defeat caching |

### 4.3 Phase 1 Expected Impact

| Scenario | Before | After |
|---|---|---|
| Cold build (first run / script changed) | ~45 min | ~25–35 min |
| Warm build (script unchanged) | ~45 min | ~5 min |

---

## 5. Phase 2: Reduce GPU Architectures

### 5.1 Current Architecture Targets

The build currently compiles HIP kernels for **5 AMD GPU architectures**:

| Architecture | GPU Family | Example Hardware | Market Share |
|---|---|---|---|
| `gfx1030` | RDNA 2 | RX 6800, RX 6800 XT, RX 6900 XT | High (enthusiast) |
| `gfx1031` | RDNA 2 | RX 6700 XT | Medium |
| `gfx1100` | RDNA 3 | RX 7900 XTX, RX 7900 XT | High (current-gen flagship) |
| `gfx1101` | RDNA 3 | RX 7800 XT, RX 7700 XT | Medium |
| `gfx1102` | RDNA 3 | RX 7600 | Medium (budget) |

HIP kernel compilation scales **linearly** with the number of target architectures. Each architecture requires a separate compilation pass for all GPU kernels in ORT + MIGraphX. Reducing from 5 to 2 architectures cuts HIP compilation time by ~60%.

### 5.2 Recommended Architecture Sets

#### Option A: Two-architecture default (recommended)

Target the two most common flagship GPUs — one per generation:

```
gfx1030;gfx1100
```

- **gfx1030** — covers the entire RDNA 2 high-end lineup (6800/6900 series). RDNA 2 is binary-compatible within the `gfx103x` family, so `gfx1030` code runs on `gfx1031`/`gfx1032` with no performance penalty.
- **gfx1100** — covers the RDNA 3 flagship (7900 series). Similarly, `gfx1100` code runs on `gfx1101`/`gfx1102` via the ISA compatibility layer.

**Estimated savings:** ~60% reduction in HIP compile time (~10–14 min saved on cold builds).

#### Option B: Single-architecture (aggressive)

Target only the current-gen flagship:

```
gfx1100
```

**Estimated savings:** ~80% reduction in HIP compile time (~14–20 min saved). Only viable if RDNA 2 support is not required.

#### Option C: Three-architecture (balanced)

```
gfx1030;gfx1100;gfx1101
```

Adds the popular mid-range RDNA 3 chip. **Estimated savings:** ~40% reduction.

### 5.3 Implementation

#### 5.3.1 Make architectures configurable via environment variable

Already done in the updated `compile_onnx_rocm_docker.sh` (Section 4.1.3):

```bash
HIP_ARCHS="${HIP_ARCHITECTURES:-gfx1030;gfx1031;gfx1100;gfx1101;gfx1102}"
```

The default remains all 5 architectures for backward compatibility. The CI workflow overrides this via a Docker `-e` flag.

#### 5.3.2 Update `build-rocm-linux.yml` — pass architecture env var

```yaml
      - name: Compile Native ROCm Libs (Docker)
        if: steps.ort-cache.outputs.cache-hit != 'true'
        env:
          HIP_ARCHITECTURES: "gfx1030;gfx1100"
        run: |
          mkdir -p artifacts
          docker run --rm \
            -e HIP_ARCHITECTURES="${{ env.HIP_ARCHITECTURES }}" \
            -v ${{ github.workspace }}:/code \
            -w /code \
            ghcr.io/intel-agency/inference-engine-rocm/rocm-build:latest \
            /code/scripts/compile_onnx_rocm_docker.sh
```

#### 5.3.3 Make architectures configurable via repository variable

For flexibility without code changes, use a repository variable:

```yaml
      - name: Compile Native ROCm Libs (Docker)
        if: steps.ort-cache.outputs.cache-hit != 'true'
        run: |
          mkdir -p artifacts
          docker run --rm \
            -e HIP_ARCHITECTURES="${{ vars.HIP_ARCHITECTURES || 'gfx1030;gfx1100' }}" \
            -v ${{ github.workspace }}:/code \
            -w /code \
            ghcr.io/intel-agency/inference-engine-rocm/rocm-build:latest \
            /code/scripts/compile_onnx_rocm_docker.sh
```

Set `HIP_ARCHITECTURES` in repository settings → Variables → Actions to override without modifying the workflow file.

#### 5.3.4 Update cache key to include architecture set

The cache key must account for the architecture set — different archs produce different binaries:

```yaml
      - name: Restore ORT build cache
        uses: actions/cache@v4
        id: ort-cache
        with:
          path: build-cache
          key: ort-rocm-${{ vars.HIP_ARCHITECTURES || 'gfx1030;gfx1100' }}-${{ hashFiles('scripts/compile_onnx_rocm_docker.sh', 'Dockerfile.rocm-build') }}
```

#### 5.3.5 Document supported architectures

Add to `README.md` or `ARCHITECTURE.md`:

```markdown
### Supported GPU Architectures

The CI build targets the following AMD GPU architectures by default:

| Architecture | GPU Family | Example Hardware |
|---|---|---|
| `gfx1030` | RDNA 2 | RX 6800/6900 series |
| `gfx1100` | RDNA 3 | RX 7900 series |

Due to ISA compatibility, `gfx1030` binaries run on all `gfx103x` GPUs and
`gfx1100` binaries run on all `gfx110x` GPUs.

To build for additional architectures, set the `HIP_ARCHITECTURES`
repository variable (e.g., `gfx1030;gfx1031;gfx1100;gfx1101;gfx1102`).
```

### 5.4 Phase 2 Expected Impact

| Scenario | Before (Phase 1 alone) | After (Phase 1 + 2) |
|---|---|---|
| Cold build (5 archs) | ~25–35 min | — |
| Cold build (2 archs) | — | ~15–22 min |
| Warm build (cached) | ~5 min | ~5 min (no change) |

---

## 6. Combined Impact Summary

| Scenario | Current | Phase 1 Only | Phase 1 + 2 |
|---|---|---|---|
| Cold build (script changed) | ~45 min | ~25–35 min | ~15–22 min |
| Warm build (script unchanged) | ~45 min | ~5 min | ~5 min |

---

## 7. Implementation Checklist

### Phase 1: Custom Docker Image + Build Caching

- [ ] Create `Dockerfile.rocm-build` at repo root
- [ ] Create `.github/workflows/build-docker-image.yml`
- [ ] Push initial image to GHCR via manual `workflow_dispatch`
- [ ] Update `scripts/compile_onnx_rocm_docker.sh` to use pre-baked image paths
- [ ] Update `build-rocm-linux.yml` — replace base image with GHCR reference
- [ ] Update `build-rocm-linux.yml` — add `actions/cache` for build artifacts
- [ ] Test cold build (clear cache, run workflow)
- [ ] Test warm build (run workflow again, verify cache hit)
- [ ] Verify all downstream jobs (`pack-nuget`, `validate-native`) still pass

### Phase 2: Reduce GPU Architectures

- [ ] Decide on default architecture set (recommend: `gfx1030;gfx1100`)
- [ ] Update `compile_onnx_rocm_docker.sh` — read `HIP_ARCHITECTURES` env var with fallback
- [ ] Update `build-rocm-linux.yml` — pass `HIP_ARCHITECTURES` to Docker via `-e`
- [ ] (Optional) Add `HIP_ARCHITECTURES` repository variable for configurability
- [ ] Update cache key to include architecture set
- [ ] Test cold build with reduced architectures, verify `.so` files load correctly
- [ ] Update documentation (README/ARCHITECTURE.md) with supported GPU list
- [ ] Verify integration tests pass with reduced architecture binaries

---

## 8. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| GHCR image push fails (permissions) | Phase 1 blocked | Ensure `packages: write` permission on the image build workflow; use `GITHUB_TOKEN` |
| Stale Docker image after ORT version bump | Build uses wrong source | Image rebuild is triggered by `Dockerfile.rocm-build` changes; include ORT tag in image tag |
| Cache evicted under 10 GB limit | Warm builds fall back to cold | Monitor cache usage; `.so` files are ~300 MB — well within limit |
| GHCR image pull rate-limited | Build fails intermittently | Authenticated pulls via `docker/login-action` get higher rate limits |
| `cp -a` of ORT source is slow | Phase 1 still slow | Consider mounting `/opt/onnxruntime` read-only instead of copying |
| Reduced archs break on older/newer GPUs | Users with unsupported GPUs get runtime errors | ISA compatibility within families covers most cases; document override via repo variable |
| Cache key mismatch after arch change | Stale cached binaries served | Cache key includes architecture set (Section 5.3.4) |

---

## 9. Dependencies & Sequencing

```
Phase 1a: Dockerfile.rocm-build
    │
    ▼
Phase 1b: build-docker-image.yml → push to GHCR
    │
    ▼
Phase 1c: Update compile script + build-rocm-linux.yml
    │
    ▼
Phase 1d: Add actions/cache to build-rocm job
    │
    ▼
Phase 1e: Test cold + warm builds
    │
    ▼
Phase 2a: Decide architecture set + update compile script
    │
    ▼
Phase 2b: Update workflow to pass HIP_ARCHITECTURES
    │
    ▼
Phase 2c: Update cache key + test
```

Phase 1 steps (a–d) should be done in a single PR. Phase 2 is a separate, smaller PR that builds on Phase 1.
