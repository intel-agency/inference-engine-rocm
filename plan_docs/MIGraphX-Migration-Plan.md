# MIGraphX Migration Plan

## Executive Summary

Migrate from the deprecated ROCm Execution Provider to AMD's recommended MIGraphX Execution Provider. This is a **breaking change** for consumers using the ROCm EP API directly, but provides a future-proof path aligned with AMD's roadmap.

**Current State:**
- ORT v1.19.2 + ROCm 6.0.2
- Uses `--use_rocm` build flag
- Produces `libonnxruntime_providers_rocm.so`
- Consumers use `ROCmExecutionProvider` API

**Target State:**
- ORT v1.23.2 + ROCm 7.2.1 (or 7.2.4)
- Uses `--use_migraphx` build flag
- Produces `libonnxruntime_providers_migraphx.so`
- Consumers use `MIGraphXExecutionProvider` API

---

## Phase 1: Build System Migration

### 1.1 Update Compilation Script

**File:** `scripts/compile_onnx_rocm_docker.sh`

**Changes:**
```bash
# Line 6: Update ORT tag
ORT_TAG="v1.23.2"  # was v1.19.2

# Line 121-133: Update build flags
./build.sh \
    --config Release \
    --build_shared_lib \
    --use_migraphx \                    # was --use_rocm
    --rocm_home "$ROCM_HOME" \
    --skip_tests \
    --skip_submodule_sync \
    --parallel \
    --allow_running_as_root \
    --cmake_extra_defines CMAKE_HIP_ARCHITECTURES="gfx1030;gfx1031;gfx1100;gfx1101;gfx1102" \
    --cmake_extra_defines FETCHCONTENT_SOURCE_DIR_EIGEN="$EIGEN_SRC_DIR"

# Line 137-138: Update artifact names
cp build/Linux/Release/libonnxruntime.so /code/artifacts/
cp build/Linux/Release/libonnxruntime_providers_migraphx.so /code/artifacts/  # was providers_rocm.so
```

**Complexity:** LOW  
**Risk:** MEDIUM - Build may fail due to MIGraphX-specific dependencies  
**Estimated Time:** 2-3 hours (including build time)

### 1.2 Update CI/CD Workflow

**File:** `.github/workflows/build-rocm-linux.yml`

**Changes:**
```yaml
# Line 38-43: Update Docker image
- name: Compile Native ROCm Libs (Docker)
  run: |
    mkdir -p artifacts
    # Update to ROCm 7.2.1 or 7.2.4
    docker run --rm \
      -v ${{ github.workspace }}:/code \
      -w /code \
      rocm/dev-ubuntu-22.04:7.2.1 \    # was 6.0.2
      /code/scripts/compile_onnx_rocm_docker.sh

# Line 77-78: Update artifact injection
- name: Inject Native Assets
  run: |
    echo "Injecting ROCm libraries into package structure..."
    TARGET_DIR="InferenceEngine.Core/runtimes/linux-x64/native"
    mkdir -p "$TARGET_DIR"
    cp temp_artifacts/libonnxruntime.so "$TARGET_DIR/"
    cp temp_artifacts/libonnxruntime_providers_migraphx.so "$TARGET_DIR/"  # was providers_rocm.so
    ls -lh "$TARGET_DIR"
```

**Complexity:** LOW  
**Risk:** LOW  
**Estimated Time:** 30 minutes

### 1.3 Update Build Transitive Targets

**File:** `InferenceEngine.Core/buildTransitive/InferenceEngine.ROCm.Runtime.linux-x64.targets`

**Decision Point:** Should we rename the package to reflect MIGraphX?

**Option A: Keep Current Package Name** (Recommended for continuity)
- Package ID: `InferenceEngine.ROCm.Runtime.linux-x64` (unchanged)
- Update comments to reflect MIGraphX
- Consumers see no change in package reference

**Option B: Rename to MIGraphX** (Cleaner but breaking)
- Package ID: `InferenceEngine.MIGraphX.Runtime.linux-x64`
- Requires consumer migration
- Clearer about what's actually being used

**Recommendation:** Option A for now, with clear documentation that it uses MIGraphX internally.

**Changes (Option A):**
```xml
<!-- Update comments only -->
<!--
  This targets file ensures InferenceEngine.ROCm.Runtime.linux-x64's native assets
  take precedence over ALL conflicting native assets from transitive dependencies.
  
  Note: This package uses AMD's MIGraphX Execution Provider (successor to ROCm EP).
-->

<!-- Line 24: Update package ID check if renaming -->
<!-- Keep as-is for Option A -->
```

**Complexity:** LOW (Option A) / MEDIUM (Option B)  
**Risk:** LOW  
**Estimated Time:** 15 minutes

---

## Phase 2: Integration Test Updates

### 2.1 Update Test Assertions

**File:** `InferenceEngine.Core.IntegrationTests/NativeLibraryValidationTests.cs`

**Changes:**
```csharp
// Line 93-97: Update library name check
[Fact]
public void LibOnnxRuntimeProvidersMIGraphX_Exists()  // was ProvidersRocm
{
    var path = Path.Combine(GetNativeLibsDir(), "libonnxruntime_providers_migraphx.so");  // was providers_rocm.so
    Assert.True(File.Exists(path), $"Missing: {path}");
}

// Line 100-109: Update both libs check
[Fact]
public void BothLibs_AreNonEmpty()
{
    var dir = GetNativeLibsDir();
    foreach (var name in new[] { "libonnxruntime.so", "libonnxruntime_providers_migraphx.so" })  // was providers_rocm.so
    {
        var info = new FileInfo(Path.Combine(dir, name));
        Assert.True(info.Exists && info.Length > 1_000_000,
            $"{name}: expected > 1 MB, got {info.Length} bytes");
    }
}

// Line 153-164: Update symbol export test
[Fact]
[Trait("Category", "LinuxOnly")]
public void LibOnnxRuntimeProvidersMIGraphX_ExportsMIGraphXProvider()  // was RocmProvider
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;
    var path = Path.Combine(GetNativeLibsDir(), "libonnxruntime_providers_migraphx.so");  // was providers_rocm.so
    var output = RunCommand("nm", $"-D --defined-only {path}");
    // MIGraphX EP symbols
    Assert.True(
        output.Contains("OrtSessionOptionsAppendExecutionProvider_MIGraphX") ||
        output.Contains("GetApi") ||
        output.Contains("migraphx"),
        $"Expected MIGraphX EP symbols not found in nm output. First 500 chars:\n{output[..Math.Min(500, output.Length)]}");
}

// Line 212-236: Update EP registration test
[Fact]
[Trait("Category", "LinuxOnly")]
public void InferenceSession_WithMIGraphXEP_ThrowsCleanExceptionNotCrash()  // was RocmEP
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;
    Assert.True(File.Exists(ModelPath), $"identity.onnx not found at: {ModelPath}");

    try
    {
        using var opts = new SessionOptions();
        var migraphxOptions = new System.Collections.Generic.Dictionary<string, string>
        {
            { "device_id", "0" }
        };
        opts.AppendExecutionProvider("MIGraphXExecutionProvider", migraphxOptions);  // was ROCmExecutionProvider
        using var session = new InferenceSession(ModelPath, opts);
        // If we reach here, MIGraphX is actually available — also a pass
    }
    catch (OnnxRuntimeException)
    {
        // Expected on runners without AMD GPU — clean managed exception = pass
    }
}
```

**Complexity:** LOW  
**Risk:** LOW  
**Estimated Time:** 1 hour

### 2.2 Update Test Project Dependencies

**File:** `InferenceEngine.Core.IntegrationTests/InferenceEngine.Core.IntegrationTests.csproj`

**Changes:**
```xml
<!-- Line 22: Update ORT version to match build -->
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.23.2" />  <!-- was 1.24.1 -->
```

**Note:** We're using ORT 1.23.2 for the build, so tests should reference the same version for consistency.

**Complexity:** LOW  
**Risk:** LOW  
**Estimated Time:** 5 minutes

---

## Phase 3: Documentation Updates

### 3.1 Update README.md

**Changes:**
- Update package description to mention MIGraphX
- Add migration guide for consumers using ROCm EP API
- Update version compatibility matrix
- Clarify that this is the successor to ROCm EP

**Key Sections to Add:**
```markdown
## Migration from ROCm EP

This package now uses AMD's MIGraphX Execution Provider (the official successor to ROCm EP).

### API Changes

**Before (ROCm EP):**
```csharp
opts.AppendExecutionProvider("ROCmExecutionProvider", new Dictionary<string, string> { { "device_id", "0" } });
```

**After (MIGraphX EP):**
```csharp
opts.AppendExecutionProvider("MIGraphXExecutionProvider", new Dictionary<string, string> { { "device_id", "0" } });
```

### Version Compatibility

| Package Version | ORT Version | ROCm Version | Execution Provider |
|----------------|-------------|--------------|-------------------|
| 1.23.2.x       | 1.23.2      | 7.2.1        | MIGraphX          |
| 1.19.2.x       | 1.19.2      | 6.0.2        | ROCm (deprecated) |
```

**Complexity:** LOW  
**Risk:** LOW  
**Estimated Time:** 1 hour

### 3.2 Update ARCHITECTURE.md

**Changes:**
- Update "Native Gap" section to mention MIGraphX
- Update build system details
- Update consumer usage examples
- Add MIGraphX-specific configuration options

**Complexity:** LOW  
**Risk:** LOW  
**Estimated Time:** 1 hour

### 3.3 Update AGENTS.md

**Changes:**
- Update version references
- Update build commands if needed
- Add MIGraphX to conventions

**Complexity:** LOW  
**Risk:** LOW  
**Estimated Time:** 30 minutes

---

## Phase 4: Package Metadata

### 4.1 Update Package Description

**File:** `InferenceEngine.Core/InferenceEngine.ROCm.Runtime.linux-x64.csproj`

**Changes:**
```xml
<!-- Line 17: Update description -->
<Description>ROCm-accelerated ONNX Runtime native libraries for Linux x64 (AMD GPU) using MIGraphX Execution Provider. Drop-in replacement for the CPU-only libonnxruntime.so shipped by Microsoft.ML.OnnxRuntime. Provides AMD GPU acceleration via ROCm/MIGraphX without requiring consumers to compile from source.</Description>

<!-- Line 18: Update tags -->
<PackageTags>onnxruntime;rocm;migraphx;amd;gpu;linux;native;inference;machine-learning;onnx</PackageTags>
```

**Complexity:** LOW  
**Risk:** LOW  
**Estimated Time:** 15 minutes

---

## Phase 5: Validation & Testing

### 5.1 Local Build Validation

**Steps:**
1. Run Docker build locally with ROCm 7.2.1 image
2. Verify both `.so` files are produced
3. Check file sizes (should be > 100 MB each)
4. Verify symbols with `nm -D`

**Command:**
```bash
docker run --rm -v "$(pwd)":/code -w /code rocm/dev-ubuntu-22.04:7.2.1 \
  /code/scripts/compile_onnx_rocm_docker.sh

# Verify artifacts
ls -lh artifacts/
nm -D artifacts/libonnxruntime_providers_migraphx.so | grep -i migraphx
```

**Complexity:** MEDIUM  
**Risk:** HIGH - Build may fail, require debugging  
**Estimated Time:** 2-4 hours (including build time)

### 5.2 Integration Test Validation

**Steps:**
1. Copy artifacts to `InferenceEngine.Core/runtimes/linux-x64/native/`
2. Run `dotnet test` on integration tests
3. Verify all tests pass

**Command:**
```bash
cp artifacts/*.so InferenceEngine.Core/runtimes/linux-x64/native/
dotnet test InferenceEngine.Core.IntegrationTests/
```

**Complexity:** LOW  
**Risk:** LOW  
**Estimated Time:** 30 minutes

### 5.3 Consumer API Validation

**Steps:**
1. Create test consumer project
2. Reference the package
3. Test MIGraphX EP registration (will fail gracefully without GPU)
4. Verify CPU fallback works

**Complexity:** LOW  
**Risk:** LOW  
**Estimated Time:** 1 hour

---

## Phase 6: Release Strategy

### 6.1 Version Bump

**Decision:** Should this be a major version bump?

**Recommendation:** Yes, treat as breaking change
- Current: `1.19.2.x`
- New: `2.0.0.x` or `1.23.2.x` (if following ORT version)

**Rationale:**
- Different execution provider (ROCm → MIGraphX)
- Different API for consumers
- Different underlying library names

### 6.2 Release Notes

**Key Points:**
- **BREAKING:** Migrated from ROCm EP to MIGraphX EP
- Updated ORT from 1.19.2 to 1.23.2
- Updated ROCm from 6.0.2 to 7.2.1
- Consumers must update API calls from `ROCmExecutionProvider` to `MIGraphXExecutionProvider`
- Added support for newer GPU architectures (gfx1101, gfx1102)

### 6.3 Deprecation Notice

**For Previous Version:**
- Tag last ROCm EP version as deprecated on NuGet
- Add deprecation notice to README
- Point users to MIGraphX version

---

## Risk Assessment

### High Risk
- **Build Failure:** MIGraphX may have different dependencies or build requirements
  - **Mitigation:** Test build locally before CI changes
  - **Fallback:** Revert to ROCm EP if MIGraphX build fails

### Medium Risk
- **Consumer Breaking Change:** API change may break downstream projects
  - **Mitigation:** Clear migration guide, deprecation notice
  - **Fallback:** Maintain both ROCm and MIGraphX packages temporarily

### Low Risk
- **Test Failures:** Integration tests may need adjustment
  - **Mitigation:** Update tests incrementally
  - **Fallback:** Disable failing tests temporarily with TODO comments

---

## Timeline Estimate

| Phase | Estimated Time | Dependencies |
|-------|---------------|--------------|
| Phase 1: Build System | 3-4 hours | None |
| Phase 2: Integration Tests | 1.5 hours | Phase 1 |
| Phase 3: Documentation | 2.5 hours | Phase 1 |
| Phase 4: Package Metadata | 15 minutes | None |
| Phase 5: Validation | 3-5 hours | Phase 1-4 |
| Phase 6: Release | 1 hour | Phase 5 |
| **Total** | **11-14 hours** | |

**Recommended Approach:** Execute over 2-3 days to allow for build debugging and validation.

---

## Success Criteria

1. ✅ Docker build completes successfully with ROCm 7.2.1
2. ✅ Both `.so` files are produced and > 100 MB
3. ✅ Integration tests pass (all or critical path)
4. ✅ MIGraphX EP symbols are present in provider library
5. ✅ Consumer project can load package and register MIGraphX EP
6. ✅ Documentation is updated with migration guide
7. ✅ Package is published to GitHub Packages (dev branch)

---

## Rollback Plan

If migration fails:
1. Revert all changes to build script and CI
2. Tag current version as "last ROCm EP version"
3. Create separate branch for MIGraphX experimentation
4. Continue supporting ROCm EP until stable MIGraphX build achieved

---

## References

- [MIGraphX Execution Provider Documentation](https://onnxruntime.ai/docs/execution-providers/MIGraphX-ExecutionProvider.html)
- [ROCm EP Deprecation Notice](https://onnxruntime.ai/docs/execution-providers/ROCm-ExecutionProvider.html)
- [ORT 1.23.2 Release Notes](https://github.com/microsoft/onnxruntime/releases/tag/v1.23.2)
- [ROCm 7.2.1 Release Notes](https://github.com/ROCm/ROCm/releases/tag/rocm-7.2.1)
