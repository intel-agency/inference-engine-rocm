# Multi-Version Branching & Package Publish Strategy

## Executive Summary

This plan addresses how to maintain multiple ORT + ROCm version couplets simultaneously, allowing consumers to pin to specific version combinations while the project evolves. Each couplet represents a tested, compatible pairing of ONNX Runtime and ROCm/MIGraphX.

**Problem:** The current setup tracks a single version couplet (ORT 1.19.2 + ROCm 6.0.2). As we migrate to MIGraphX and future ORT/ROCm releases arrive, consumers may need to stay on older couplets for stability while newer ones are validated.

---

## Current State Analysis

### Existing Branching Model

```
development → staging → release
```

- Single `VERSION_PREFIX` GitHub variable (currently `1.19.2`)
- Version computed as `{VERSION_PREFIX}-{suffix}.{run_number}`
- All branches produce packages with the same ORT version prefix
- No mechanism to build/publish multiple ORT versions concurrently

### Existing Versioning

| Branch | Format | Example |
|--------|--------|---------|
| `development` | `{VERSION_PREFIX}-dev.{run}` | `1.19.2-dev.42` |
| `staging` | `{VERSION_PREFIX}-rc.{run}` | `1.19.2-rc.58` |
| `release` | `{VERSION_PREFIX}.{run}` | `1.19.2.71` |

---

## Proposed Version Couplets

| Couplet ID | ORT Version | ROCm Version | Execution Provider | Status |
|------------|-------------|--------------|-------------------|--------|
| `v1-rocm` | 1.19.2 | 6.0.2 | ROCm EP | Current (legacy) |
| `v2-migraphx` | 1.22.1 | 7.0 | ROCm EP (last) | Transitional |
| `v3-migraphx` | 1.23.2 | 7.2.1 | MIGraphX | Target |
| `v4-migraphx` | 1.26.0 | 7.2.4 | MIGraphX | Future |

---

## Strategy Options

### Option A: Long-Lived Version Branches

```
main
├── version/1.19.2-rocm6.0.2      (legacy, maintenance only)
├── version/1.22.1-rocm7.0        (transitional)
├── version/1.23.2-migraphx7.2.1  (current stable)
└── development → staging → release  (tracks latest couplet)
```

**How it works:**
- Each version couplet gets a permanent branch: `version/{ORT}-{ROCM}`
- `development/staging/release` always track the **latest** couplet
- Version branches receive only security patches and bug fixes
- Each version branch has its own CI workflow trigger

**Package IDs:** Single package ID, version-differentiated
```
InferenceEngine.ROCm.Runtime.linux-x64  v1.19.2.42   (from version/1.19.2-rocm6.0.2)
InferenceEngine.ROCm.Runtime.linux-x64  v1.22.1.15   (from version/1.22.1-rocm7.0)
InferenceEngine.ROCm.Runtime.linux-x64  v1.23.2.8    (from version/1.23.2-migraphx7.2.1)
```

**Feasibility:** HIGH  
**Complexity:** MEDIUM  

| Aspect | Assessment |
|--------|-----------|
| CI/CD changes | Moderate — need per-branch `VERSION_PREFIX` and Docker image pinning |
| Git workflow | Simple — standard long-lived branches |
| NuGet publishing | Simple — same package ID, different versions |
| Consumer experience | Good — consumers pin to specific version |
| Maintenance burden | Medium — each branch needs independent patching |
| Disk/CI cost | Low — branches share most code, only build config differs |

**Pros:**
- Consumers use a single package ID and pin version
- NuGet's built-in version resolution handles compatibility
- Simple mental model: "I'm on ORT 1.19.2, so I use package version 1.19.2.x"
- No package proliferation

**Cons:**
- Long-lived branches diverge over time (merge conflicts for shared fixes)
- `VERSION_PREFIX` must be set per-branch (cannot use single repo variable)
- Build script must be self-contained per branch (Docker image tag hardcoded)
- Patching a shared bug across N version branches requires N cherry-picks

---

### Option B: Separate Package IDs Per Couplet

```
main
├── development → staging → release   (tracks latest couplet)
└── (tags for historical couplets)
```

**How it works:**
- Each couplet produces a **different NuGet package ID**
- Single branch tracks latest; older couplets are tag-only (no active maintenance)
- Consumers explicitly reference the package ID for their couplet

**Package IDs:**
```
InferenceEngine.ROCm.Runtime.linux-x64              v1.23.2.8   (latest, MIGraphX)
InferenceEngine.ROCm1192.Runtime.linux-x64          v1.19.2.42  (legacy ROCm EP)
InferenceEngine.ROCm1221.Runtime.linux-x64          v1.22.1.15  (transitional)
```

**Feasibility:** MEDIUM  
**Complexity:** HIGH  

| Aspect | Assessment |
|--------|-----------|
| CI/CD changes | High — separate workflows per package ID, or parameterized matrix |
| Git workflow | Simple — single branch, tags for history |
| NuGet publishing | Complex — multiple package IDs to manage, deprecate, document |
| Consumer experience | Poor — must know which package ID to reference, migration requires package swap |
| Maintenance burden | High — each package ID needs its own deprecation lifecycle |
| Disk/CI cost | Medium — separate build pipelines |

**Pros:**
- No branch divergence
- Consumers can reference multiple couplets side-by-side (testing)
- Clear lifecycle per package (deprecate old ones explicitly)

**Cons:**
- Package ID proliferation confuses consumers
- Requires consumers to change `<PackageReference>` when upgrading couplet
- NuGet.org doesn't easily show relationship between related packages
- More complex CI (matrix or separate workflows)
- `buildTransitive` targets must be duplicated per package ID

---

### Option C: Single Branch + Matrix CI (Recommended)

```
main
├── development → staging → release   (single branch)
└── (version couplets defined in CI matrix config)
```

**How it works:**
- Single branch, single codebase
- A **couplet manifest** file defines all supported ORT + ROCm combinations
- CI uses a **matrix strategy** to build all couplets in parallel
- Each couplet produces a versioned package under the **same package ID**
- `VERSION_PREFIX` is set per-matrix-entry, not as a repo variable

**Couplet Manifest:** `couplets.json`
```json
{
  "couplets": [
    {
      "id": "rocm-legacy",
      "ort_tag": "v1.19.2",
      "rocm_image": "rocm/dev-ubuntu-22.04:6.0.2",
      "build_flag": "--use_rocm",
      "provider_lib": "libonnxruntime_providers_rocm.so",
      "ep_name": "ROCmExecutionProvider",
      "version_prefix": "1.19.2",
      "hip_archs": "gfx1030;gfx1031;gfx1100",
      "active": false,
      "deprecated": true
    },
    {
      "id": "migraphx-stable",
      "ort_tag": "v1.23.2",
      "rocm_image": "rocm/dev-ubuntu-22.04:7.2.1",
      "build_flag": "--use_migraphx",
      "provider_lib": "libonnxruntime_providers_migraphx.so",
      "ep_name": "MIGraphXExecutionProvider",
      "version_prefix": "1.23.2",
      "hip_archs": "gfx1030;gfx1031;gfx1100;gfx1101;gfx1102",
      "active": true,
      "deprecated": false
    },
    {
      "id": "migraphx-latest",
      "ort_tag": "v1.26.0",
      "rocm_image": "rocm/dev-ubuntu-22.04:7.2.4",
      "build_flag": "--use_migraphx",
      "provider_lib": "libonnxruntime_providers_migraphx.so",
      "ep_name": "MIGraphXExecutionProvider",
      "version_prefix": "1.26.0",
      "hip_archs": "gfx1030;gfx1031;gfx1100;gfx1101;gfx1102;gfx1150;gfx1151",
      "active": true,
      "deprecated": false
    }
  ]
}
```

**CI Workflow Changes:**
```yaml
jobs:
  build-rocm:
    strategy:
      matrix:
        couplet:
          - id: rocm-legacy
            ort_tag: v1.19.2
            rocm_image: rocm/dev-ubuntu-22.04:6.0.2
            version_prefix: "1.19.2"
          - id: migraphx-stable
            ort_tag: v1.23.2
            rocm_image: rocm/dev-ubuntu-22.04:7.2.1
            version_prefix: "1.23.2"
          - id: migraphx-latest
            ort_tag: v1.26.0
            rocm_image: rocm/dev-ubuntu-22.04:7.2.4
            version_prefix: "1.26.0"
    steps:
      - name: Compile Native Libs
        run: |
          docker run --rm \
            -v ${{ github.workspace }}:/code \
            -w /code \
            -e ORT_TAG=${{ matrix.couplet.ort_tag }} \
            ${{ matrix.couplet.rocm_image }} \
            /code/scripts/compile_onnx_rocm_docker.sh
```

**Package IDs:** Single package ID, version-differentiated
```
InferenceEngine.ROCm.Runtime.linux-x64  v1.19.2.42   (ROCm EP, deprecated)
InferenceEngine.ROCm.Runtime.linux-x64  v1.23.2.8    (MIGraphX, stable)
InferenceEngine.ROCm.Runtime.linux-x64  v1.26.0.3    (MIGraphX, latest)
```

**Feasibility:** HIGH  
**Complexity:** MEDIUM-HIGH  

| Aspect | Assessment |
|--------|-----------|
| CI/CD changes | High — matrix strategy, parameterized build script, per-couplet artifacts |
| Git workflow | Simple — single branch, no divergence |
| NuGet publishing | Simple — same package ID, NuGet handles version resolution |
| Consumer experience | Excellent — consumers pin version, NuGet resolves |
| Maintenance burden | Low — single codebase, shared fixes apply to all couplets |
| Disk/CI cost | High — N parallel builds, each 30-60 min |

**Pros:**
- Single source of truth (one branch, one codebase)
- Shared bug fixes apply to all couplets automatically
- Consumers use one package ID and pin version
- Adding a new couplet = adding a matrix entry + manifest entry
- `couplets.json` serves as living documentation
- Build script becomes parameterized (cleaner, more reusable)

**Cons:**
- CI build time multiplied by number of active couplets
- Build script must accept environment variables for all couplet-specific config
- Integration tests must be parameterized per couplet (different EP names, lib names)
- Matrix failures may be couplet-specific (harder to diagnose)
- GitHub Actions matrix has a 256-job limit (not a concern here)

---

## Detailed Feasibility Analysis

### Build Script Parameterization

**Current:** Hardcoded values in `compile_onnx_rocm_docker.sh`  
**Required:** Script must accept env vars for ORT tag, build flag, provider lib name, HIP archs

**Changes needed:**
```bash
# Replace hardcoded values with env vars (with defaults)
ORT_TAG="${ORT_TAG:-v1.23.2}"
BUILD_FLAG="${BUILD_FLAG:---use_migraphx}"
PROVIDER_LIB="${PROVIDER_LIB:-libonnxruntime_providers_migraphx.so}"
HIP_ARCHS="${HIP_ARCHS:-gfx1030;gfx1031;gfx1100;gfx1101;gfx1102}"
```

**Complexity:** LOW  
**Risk:** LOW — additive change, defaults preserve current behavior  
**Feasibility:** HIGH

### Integration Test Parameterization

**Current:** Tests hardcode `libonnxruntime_providers_rocm.so` and `ROCmExecutionProvider`  
**Required:** Tests must accept env vars or config for EP name and provider lib

**Approach:**
```csharp
// Read from environment or config file
private static string ProviderLibName =>
    Environment.GetEnvironmentVariable("PROVIDER_LIB") ?? "libonnxruntime_providers_migraphx.so";

private static string ExecutionProviderName =>
    Environment.GetEnvironmentVariable("EP_NAME") ?? "MIGraphXExecutionProvider";
```

**Complexity:** LOW  
**Risk:** LOW  
**Feasibility:** HIGH

### CI Artifact Isolation

**Current:** Single `native-rocm-libs` artifact  
**Required:** Per-couplet artifacts to prevent overwriting

**Approach:**
```yaml
- name: Upload Native Artifacts
  uses: actions/upload-artifact@v4
  with:
    name: native-libs-${{ matrix.couplet.id }}
    path: artifacts/*.so
```

**Complexity:** LOW  
**Risk:** LOW  
**Feasibility:** HIGH

### NuGet Version Collision

**Risk:** Two couplets producing the same version prefix could collide.

**Mitigation:** Each couplet has a unique `version_prefix` (the ORT version). Since ORT versions are unique, collisions are impossible.

**Complexity:** NONE  
**Risk:** NONE  
**Feasibility:** HIGH

### Selective Build (Cost Control)

**Problem:** Building all couplets on every push is expensive (30-60 min each).

**Solutions:**

1. **Path-based triggers:** Only build couplets whose config changed
2. **Manual dispatch:** `workflow_dispatch` with couplet selection
3. **Active-only builds:** Only build `active: true` couplets on push; build all on release

**Recommended approach:**
```yaml
on:
  push:
    branches: [development, staging, release]
    # Only build active couplets on push
  workflow_dispatch:
    inputs:
      couplet_id:
        description: 'Specific couplet to build (leave empty for all active)'
        required: false
```

**Complexity:** MEDIUM  
**Risk:** LOW  
**Feasibility:** HIGH

---

## Recommended Strategy: Option C (Matrix CI)

### Rationale

| Criterion | Option A (Branches) | Option B (Package IDs) | Option C (Matrix) |
|-----------|:---:|:---:|:---:|
| Code maintenance | Medium | Low | **Low** |
| Consumer UX | Good | Poor | **Excellent** |
| CI complexity | Low | High | Medium |
| Adding new couplet | Medium | High | **Low** |
| Deprecation lifecycle | Manual | Explicit | **Config-driven** |
| Shared bug fixes | Cherry-pick | Cherry-pick | **Automatic** |
| Build cost | Low | High | Medium-High |

### Implementation Plan

#### Step 1: Parameterize Build Script
**Complexity:** LOW | **Time:** 1 hour

Convert `compile_onnx_rocm_docker.sh` to accept env vars for all couplet-specific values. Keep current defaults for backward compatibility.

#### Step 2: Create Couplet Manifest
**Complexity:** LOW | **Time:** 30 minutes

Create `couplets.json` at repo root defining all supported couplets.

#### Step 3: Parameterize Integration Tests
**Complexity:** LOW | **Time:** 1 hour

Update tests to read EP name and provider lib from environment variables.

#### Step 4: Update CI Workflow to Matrix
**Complexity:** MEDIUM | **Time:** 3-4 hours

Convert `build-rocm-linux.yml` to use matrix strategy. Update artifact names, version computation, and pack steps to be couplet-aware.

#### Step 5: Add Selective Build Logic
**Complexity:** MEDIUM | **Time:** 2 hours

Add `workflow_dispatch` inputs and path-based filtering to avoid building all couplets on every push.

#### Step 6: Update Documentation
**Complexity:** LOW | **Time:** 1 hour

Update README, ARCHITECTURE.md, and AGENTS.md with couplet matrix and consumer guidance.

#### Step 7: Migrate Legacy Couplet
**Complexity:** LOW | **Time:** 30 minutes

Add the current ROCm EP couplet (1.19.2 + 6.0.2) as the first matrix entry with `deprecated: true`.

**Total Estimated Time:** 9-11 hours

---

## Consumer Guidance

### Pinning to a Specific Couplet

```xml
<!-- Pin to ORT 1.19.2 + ROCm 6.0.2 (legacy ROCm EP) -->
<PackageReference Include="InferenceEngine.ROCm.Runtime.linux-x64" Version="1.19.2.*" />

<!-- Pin to ORT 1.23.2 + ROCm 7.2.1 (MIGraphX, stable) -->
<PackageReference Include="InferenceEngine.ROCm.Runtime.linux-x64" Version="1.23.2.*" />

<!-- Use latest available -->
<PackageReference Include="InferenceEngine.ROCm.Runtime.linux-x64" Version="1.*" />
```

### Upgrading Between Couplets

1. Update `<PackageReference>` version
2. If crossing ROCm EP → MIGraphX boundary (1.19.x → 1.23.x):
   - Change `ROCmExecutionProvider` → `MIGraphXExecutionProvider` in code
3. Rebuild and test

---

## Deprecation Lifecycle

```
Active → Deprecated → Archived
```

| State | CI Builds | NuGet Published | Consumer Action |
|-------|-----------|-----------------|-----------------|
| **Active** | On push + release | Yes | Use freely |
| **Deprecated** | Release only | Yes (marked deprecated) | Plan migration |
| **Archived** | Never | Unlisted | Remove reference |

Controlled via `couplets.json`:
```json
{
  "active": false,
  "deprecated": true
}
```

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| CI build time too long | Medium | Medium | Selective builds, cache Docker layers |
| Couplet config drift | Low | Low | Single manifest file, validated in CI |
| Consumer confusion on version selection | Low | Medium | Clear documentation, version table in README |
| Matrix entry breaks existing couplet | Low | High | Integration tests per couplet, fail-fast |
| NuGet version ordering issues | Low | Low | SemVer 2.0 compliant, ORT versions are naturally ordered |

---

## Open Questions

1. **Should deprecated couplets build on push?** Recommendation: No, only on release or manual dispatch.
2. **Maximum number of active couplets?** Recommendation: 3 (legacy, stable, latest).
3. **Should the package ID change for MIGraphX?** Recommendation: No, keep `InferenceEngine.ROCm.Runtime.linux-x64` for continuity. The "ROCm" in the name refers to the platform, not the EP.
4. **Should we publish a compatibility matrix on NuGet.org?** Recommendation: Yes, in the package README.
