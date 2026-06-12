# MIGraphX Migration Implementation Plan

## Overview

Migrate from deprecated ROCm Execution Provider to AMD's recommended MIGraphX Execution Provider. This is a breaking change that updates ORT from v1.19.2 to v1.24.1 and ROCm from 6.0.2 to 7.2.1.

**Decision:** Keep package name as `InferenceEngine.ROCm.Runtime.linux-x64` (Option A) for continuity.

## Implementation Status

**Status: IMPLEMENTED** (all code changes committed, pending Docker build validation)

### Commits

| # | Hash | Subject |
|---|------|---------|
| 1 | `7dae29b` | migrate build system from ROCm EP to MIGraphX EP (ORT v1.24.1, ROCm 7.2.1) |
| 2 | `c7d9422` | update integration tests and package metadata for MIGraphX EP |
| 3 | `846cf14` | update documentation for MIGraphX migration with API change guide |

### Completed Steps

- [x] **1. Build Script** (`scripts/compile_onnx_rocm_docker.sh`) — `7dae29b`
  - ORT_TAG → `v1.24.1`
  - `--use_rocm` → `--use_migraphx --migraphx_home "$ROCM_HOME"`
  - Eigen commit → `1d8b82b0740839c0de7f1242a3585e3390ff5f33` from `eigen-mirror` GitHub repo
  - Added `migraphx-dev` to apt-get install, added `gfx1101;gfx1102` architectures
  - Removed `--rocm_version` and `onnxruntime_USE_COMPOSABLE_KERNEL=OFF`
  - Output: `libonnxruntime_providers_migraphx.so`

- [x] **2. CI/CD Workflow** (`.github/workflows/build-rocm-linux.yml`) — `7dae29b`
  - Docker image → `rocm/dev-ubuntu-22.04:7.2.1`
  - Artifact copy → `libonnxruntime_providers_migraphx.so`
  - Updated MIGraphX dep comments in validate-native job

- [x] **3. Integration Tests** (`NativeLibraryValidationTests.cs`) — `c7d9422`
  - `LibOnnxRuntimeProvidersMIGraphX_Exists` — checks `providers_migraphx.so`
  - `BothLibs_AreNonEmpty` — updated library name
  - `LibOnnxRuntimeProvidersMIGraphX_IsElf64` — updated path
  - `LibOnnxRuntimeProvidersMIGraphX_ExportsMIGraphXProvider` — checks `MIGraphX` symbols
  - `InferenceSession_WithMIGraphXEP_ThrowsCleanExceptionNotCrash` — uses `MIGraphXExecutionProvider` API

- [x] **4. Test Project** (`InferenceEngine.Core.IntegrationTests.csproj`) — `c7d9422`
  - ORT NuGet → `1.24.1`

- [x] **5. Package Metadata** (`InferenceEngine.ROCm.Runtime.linux-x64.csproj`) — `c7d9422`
  - Description mentions MIGraphX Execution Provider
  - Tags include `migraphx`

- [x] **6. Build Transitive Targets** (`InferenceEngine.ROCm.Runtime.linux-x64.targets`) — `c7d9422`
  - Comments updated for MIGraphX EP, note about `providers_migraphx.so`

- [x] **7. README.md** — `846cf14`
  - Updated libs, Docker image, ORT version
  - Added migration section with API change (`ROCmExecutionProvider` → `MIGraphXExecutionProvider`)
  - Added version compatibility table

- [x] **8. ARCHITECTURE.md** — `846cf14`
  - Updated Docker image, build flags, artifact names, consumer examples

- [x] **9. AGENTS.md** — `846cf14`
  - Updated Docker image, artifact names, MIGraphX EP mention

### Pending Validation

- [ ] Docker build with `rocm/dev-ubuntu-22.04:7.2.1` (requires ROCm hardware/Docker)
- [ ] Integration tests with compiled artifacts (requires `.so` files from Docker build)
- [ ] NuGet pack with real artifacts

### Validation Commands

```bash
# 1. Docker build (requires ROCm runtime)
docker run --rm -v "$(pwd)":/code -w /code rocm/dev-ubuntu-22.04:7.2.1 \
  /code/scripts/compile_onnx_rocm_docker.sh
ls -lh artifacts/
nm -D artifacts/libonnxruntime_providers_migraphx.so | grep -i migraphx

# 2. Integration tests (after artifacts exist)
cp artifacts/*.so InferenceEngine.Core/runtimes/linux-x64/native/
dotnet test InferenceEngine.Core.IntegrationTests/

# 3. Package build
dotnet pack InferenceEngine.Core/
```

## Implementation Notes

### Key Technical Decisions

1. **Kept `--rocm_home`** alongside `--use_migraphx` — MIGraphX uses HIP/ROCm under the hood; the HIP compiler path is still needed.

2. **Removed `--rocm_version`** — This flag was specific to the ROCm EP cmake in ORT v1.19.2. MIGraphX EP in v1.24.1 auto-detects ROCm version.

3. **Removed `onnxruntime_USE_COMPOSABLE_KERNEL=OFF`** — This was a ROCm EP-specific workaround. Not needed for MIGraphX.

4. **Kept `rocm_version.h` rewrite** — Safety measure; newer ROCm images may still ship this header in a format that confuses ORT cmake.

5. **Eigen source** — Switched from GitLab (`libeigen/eigen`) to GitHub mirror (`eigen-mirror/eigen`) matching ORT v1.24.1's `cmake/deps.txt`. Commit `1d8b82b0740839c0de7f1242a3585e3390ff5f33`.

6. **GPU architectures** — Added `gfx1101` (RX 7900 XT/XTX variant) and `gfx1102` (RX 7600/7700) alongside existing `gfx1030;gfx1031;gfx1100`.

## Success Criteria

- [x] All 9 files updated and committed
- [x] `dotnet build` succeeds
- [ ] Docker build completes with ROCm 7.2.1
- [ ] Both `.so` files produced and > 100 MB
- [ ] MIGraphX EP symbols present in provider library
- [ ] Integration tests pass
- [x] Documentation updated with migration guide

## Rollback Plan

If migration fails:
1. `git revert` the 3 commits
2. Tag current version as "last ROCm EP version"
3. Create separate branch for MIGraphX experimentation
