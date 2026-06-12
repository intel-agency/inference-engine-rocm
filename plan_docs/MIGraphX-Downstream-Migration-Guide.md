# MIGraphX EP Downstream Migration Guide

## Overview

The `InferenceEngine.ROCm.Runtime.linux-x64` native NuGet package has been migrated from the **deprecated ROCm Execution Provider** (ORT v1.19.2, ROCm 6.0.2) to AMD's recommended **MIGraphX Execution Provider** (ORT v1.24.1, ROCm 7.2.1). This is a breaking change for downstream consumers.

**What changed in the native package:**

| | Old (ROCm EP) | New (MIGraphX EP) |
|---|---|---|
| ORT version | 1.19.2 | 1.24.1 |
| ROCm version | 6.0.2 | 7.2.1 |
| EP type | `ROCmExecutionProvider` | `MIGraphXExecutionProvider` |
| Provider .so | `libonnxruntime_providers_rocm.so` | `libonnxruntime_providers_migraphx.so` |
| Build flag | `--use_rocm` | `--use_migraphx` |
| .NET typed API | `OrtROCMProviderOptions` + `AppendExecutionProvider_ROCm()` | None (use generic string API) |

The downstream project `inference-engine-lib` currently pins the old package at `1.19.2-dev.28` and uses the typed ROCm EP API. This document specifies every change required to consume the new `1.24.1-dev.32` (or later) package.

---

## Required Changes

### Change 1: Update NuGet Package Version

**File:** `InferenceEngine.Core/InferenceEngine.Core.csproj`  
**Line:** 102

```xml
<!-- BEFORE -->
<PackageReference Include="InferenceEngine.ROCm.Runtime.linux-x64" Version="1.19.2-dev.28" />

<!-- AFTER -->
<PackageReference Include="InferenceEngine.ROCm.Runtime.linux-x64" Version="1.24.1-dev.32" />
```

Also update the comment block at lines 89-99 to reflect that the version is now aligned (no longer mismatched). The new package ships ORT 1.24.1 native libs, matching the base `Microsoft.ML.OnnxRuntime` 1.24.1 managed bindings. The defensive "version mismatch" comments are no longer accurate. Replace the entire comment block (lines 82-99) with:

```xml
  <!-- MIGraphX EP native libs — included unconditionally on Linux x64. The base ONNX Runtime
       libonnxruntime.so is restored from the upstream Microsoft.ML.OnnxRuntime package
       after Build (see RestoreUpstreamCpuLibOnLinux target) so the .so is safe to load on
       hosts without ROCm drivers. At runtime, BaseInferenceEngine.ConfiguredSessionOptions
       checks /dev/kfd and the HSA runtime before attempting to append the MIGraphX EP; on
       non-ROCm hosts the engine falls back to CPU automatically.

       The MIGraphX EP replaced the deprecated ROCm EP in ORT v1.24.1. The native package
       version now matches the base Microsoft.ML.OnnxRuntime managed bindings (both 1.24.1). -->
```

### Change 2: Switch EP Registration from Typed ROCm API to Generic MIGraphX API (BREAKING)

**File:** `InferenceEngine.Core/BaseInferenceEngine.cs`  
**Lines:** 289-308 (the entire Linux x64 ROCm try/catch block)

The typed API `OrtROCMProviderOptions` and `AppendExecutionProvider_ROCm()` do not exist for the MIGraphX EP. Replace with the generic string-based `AppendExecutionProvider()` API.

```csharp
// BEFORE (lines 289-308)
                _logger.LogInformation("Platform detected: Linux x64. ROCm runtime detected. Attempting ROCm provider...");

                try
                {
                    using var rocmOptions = new OrtROCMProviderOptions();
                    rocmOptions.UpdateOptions(new Dictionary<string, string>
                    {
                        ["device_id"] = _options.DeviceId.ToString(),
                        ["miopen_conv_exhaustive_search"] = "1",
                        ["arena_extend_strategy"] = "kSameAsRequested",
                    });

                    options.AppendExecutionProvider_ROCm(rocmOptions);
                    rocmProviderAppended = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ROCm provider failed to load. Falling back to CPU.");
                    options.AppendExecutionProvider_CPU();
                }

// AFTER
                _logger.LogInformation("Platform detected: Linux x64. ROCm runtime detected. Attempting MIGraphX provider...");

                try
                {
                    var migraphxOptions = new Dictionary<string, string>
                    {
                        ["device_id"] = _options.DeviceId.ToString(),
                    };

                    options.AppendExecutionProvider("MIGraphXExecutionProvider", migraphxOptions);
                    rocmProviderAppended = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MIGraphX provider failed to load. Falling back to CPU.");
                    options.AppendExecutionProvider_CPU();
                }
```

Key differences:
- `OrtROCMProviderOptions` is removed entirely — MIGraphX uses a plain `Dictionary<string, string>`
- `AppendExecutionProvider_ROCm(rocmOptions)` → `AppendExecutionProvider("MIGraphXExecutionProvider", migraphxOptions)`
- `miopen_conv_exhaustive_search` and `arena_extend_strategy` options are ROCm-EP-specific and have no MIGraphX equivalents — remove them
- MIGraphX only supports `device_id` as a configurable option
- The `using` keyword on the options object is no longer needed (Dictionary is not IDisposable)

### Change 3: Rename `libonnxruntime_providers_rocm.so` → `libonnxruntime_providers_migraphx.so` in MSBuild Symlink Targets

The new package ships `libonnxruntime_providers_migraphx.so` instead of `libonnxruntime_providers_rocm.so`. All symlinks that reference the old filename must be updated.

**File:** `InferenceEngine.Core/InferenceEngine.Core.csproj`

```xml
<!-- BEFORE (line 148) -->
    <Exec Command="ln -sf libonnxruntime_providers_rocm.so   $(OutDir)libonnxruntime_providers_rocm.dll.so"
           ContinueOnError="true" />
<!-- AFTER -->
    <Exec Command="ln -sf libonnxruntime_providers_migraphx.so   $(OutDir)libonnxruntime_providers_migraphx.dll.so"
           ContinueOnError="true" />

<!-- BEFORE (line 156) -->
    <Exec Command="ln -sf libonnxruntime_providers_rocm.so   $(OutDir)onnxruntime_providers_rocm.dll"
           ContinueOnError="true" />
<!-- AFTER -->
    <Exec Command="ln -sf libonnxruntime_providers_migraphx.so   $(OutDir)onnxruntime_providers_migraphx.dll"
           ContinueOnError="true" />
```

Also update the comment at line 160 to say `MIGraphX` instead of `ROCm`:

```xml
  <!-- BEFORE (line 160) -->
  <!-- The InferenceEngine.ROCm.Runtime.linux-x64 package ships a ROCm-enabled
  <!-- AFTER -->
  <!-- The InferenceEngine.ROCm.Runtime.linux-x64 package ships a MIGraphX-enabled
```

And line 166:

```xml
  <!-- BEFORE -->
  the provider .so (libonnxruntime_providers_rocm.so) is still
  <!-- AFTER -->
  the provider .so (libonnxruntime_providers_migraphx.so) is still
```

**File:** `InferenceEngine.Tests/InferenceEngine.Tests.csproj`

```xml
<!-- BEFORE (line 84) -->
    <Exec Command="ln -sf libonnxruntime_providers_rocm.so   $(OutDir)libonnxruntime_providers_rocm.dll.so"
           ContinueOnError="true" />
<!-- AFTER -->
    <Exec Command="ln -sf libonnxruntime_providers_migraphx.so   $(OutDir)libonnxruntime_providers_migraphx.dll.so"
           ContinueOnError="true" />

<!-- BEFORE (line 92) -->
    <Exec Command="ln -sf libonnxruntime_providers_rocm.so   $(OutDir)onnxruntime_providers_rocm.dll"
           ContinueOnError="true" />
<!-- AFTER -->
    <Exec Command="ln -sf libonnxruntime_providers_migraphx.so   $(OutDir)onnxruntime_providers_migraphx.dll"
           ContinueOnError="true" />
```

### Change 4: Update Integration Tests — Native Library Validation

**File:** `InferenceEngine.Core.IntegrationTests/NativeLibraryValidationTests.cs`

Multiple tests reference `libonnxruntime_providers_rocm.so` and the old `ROCmExecutionProvider` string.

#### 4a. Rename test method and file check (lines 108-113)

```csharp
// BEFORE
        [SkippableFact]
        public void LibOnnxRuntimeProvidersRocm_Exists()
        {
            var path = Path.Combine(GetNativeLibsDir(), "libonnxruntime_providers_rocm.so");
            Assert.True(File.Exists(path), $"Missing: {path}");
        }

// AFTER
        [SkippableFact]
        public void LibOnnxRuntimeProvidersMIGraphX_Exists()
        {
            var path = Path.Combine(GetNativeLibsDir(), "libonnxruntime_providers_migraphx.so");
            Assert.True(File.Exists(path), $"Missing: {path}");
        }
```

#### 4b. Update BothLibs_AreNonEmpty (lines 115-125)

```csharp
// BEFORE
        [SkippableFact]
        public void BothLibs_AreNonEmpty()
        {
            var dir = GetNativeLibsDir();
            foreach (var name in new[] { "libonnxruntime.so", "libonnxruntime_providers_rocm.so" })
            {
                var info = new FileInfo(Path.Combine(dir, name));
                Assert.True(info.Exists && info.Length > 1_000_000,
                    $"{name}: expected > 1 MB, got {info.Length} bytes");
            }
        }

// AFTER
        [SkippableFact]
        public void BothLibs_AreNonEmpty()
        {
            var dir = GetNativeLibsDir();
            var thresholds = new (string name, long minBytes)[]
            {
                ("libonnxruntime.so", 1_000_000),
                ("libonnxruntime_providers_migraphx.so", 100_000),
            };
            foreach (var (name, minBytes) in thresholds)
            {
                var info = new FileInfo(Path.Combine(dir, name));
                Assert.True(info.Exists && info.Length > minBytes,
                    $"{name}: expected > {minBytes} bytes, got {info.Length} bytes");
            }
        }
```

Note: The MIGraphX provider .so is a thin plugin wrapper (~550 KB) and does not meet the 1 MB threshold. Use 100 KB for provider libs.

#### 4c. Update ELF format test (lines 142-151)

```csharp
// BEFORE
        public void LibOnnxRuntimeProvidersRocm_IsElf64()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;
            var path = Path.Combine(GetNativeLibsDir(), "libonnxruntime_providers_rocm.so");
            ...

// AFTER
        public void LibOnnxRuntimeProvidersMIGraphX_IsElf64()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;
            var path = Path.Combine(GetNativeLibsDir(), "libonnxruntime_providers_migraphx.so");
            ...
```

#### 4d. Update symbol export test (lines 167-180)

```csharp
// BEFORE
        public void LibOnnxRuntimeProvidersRocm_ExportsRocmProvider()
        {
            ...
            var path = Path.Combine(GetNativeLibsDir(), "libonnxruntime_providers_rocm.so");
            var output = RunCommand("nm", $"-D --defined-only {path}");
            Assert.True(
                output.Contains("OrtSessionOptionsAppendExecutionProvider_ROCm") ||
                output.Contains("GetApi") ||
                output.Contains("rocm"),
                ...);

// AFTER
        public void LibOnnxRuntimeProvidersMIGraphX_ExportsMIGraphXProvider()
        {
            ...
            var path = Path.Combine(GetNativeLibsDir(), "libonnxruntime_providers_migraphx.so");
            var output = RunCommand("nm", $"-D --defined-only {path}");
            Assert.True(
                output.Contains("OrtSessionOptionsAppendExecutionProvider_MIGraphX") ||
                output.Contains("GetApi") ||
                output.Contains("migraphx") ||
                output.Contains("GetProvider"),
                $"Expected MIGraphX EP symbols not found in nm output. First 500 chars:\n{output[..Math.Min(500, output.Length)]}");
```

#### 4e. Update InferenceSession test (lines 226-252)

```csharp
// BEFORE
        public void InferenceSession_WithRocmEP_ThrowsCleanExceptionNotCrash()
        {
            ...
                opts.AppendExecutionProvider("ROCmExecutionProvider", rocmOptions);

// AFTER
        public void InferenceSession_WithMIGraphXEP_ThrowsCleanExceptionNotCrash()
        {
            ...
                opts.AppendExecutionProvider("MIGraphXExecutionProvider", migraphxOptions);
```

Also rename the local variable `rocmOptions` → `migraphxOptions` for clarity.

### Change 5: Update FaceDetectionEngineTests

**File:** `InferenceEngine.Tests/FaceDetectionEngineTests.cs`

#### 5a. Update `IsRocmLibPresent()` (lines 103-111)

```csharp
// BEFORE
    private static bool IsRocmLibPresent()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "libonnxruntime_providers_rocm.so"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native", "libonnxruntime_providers_rocm.so"),
        };
        return candidates.Any(File.Exists);
    }

// AFTER
    private static bool IsMIGraphXLibPresent()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "libonnxruntime_providers_migraphx.so"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native", "libonnxruntime_providers_migraphx.so"),
        };
        return candidates.Any(File.Exists);
    }
```

#### 5b. Update all callers of `IsRocmLibPresent()` (lines 174, 200)

```csharp
// BEFORE
        Skip.IfNot(IsRocmLibPresent(), "libonnxruntime_providers_rocm.so not found -- ...");

// AFTER
        Skip.IfNot(IsMIGraphXLibPresent(), "libonnxruntime_providers_migraphx.so not found -- ...");
```

#### 5c. Update test assertions and comments referencing ROCm EP (lines 194-210)

```csharp
// BEFORE (line 194)
    public async Task PredictAsync_OnLinuxX64WithAmdGpu_ShouldUseRocmProvider()

// AFTER
    public async Task PredictAsync_OnLinuxX64WithAmdGpu_ShouldUseMIGraphXProvider()

// BEFORE (lines 209-210)
        Assert.True(engine.IsRocmSessionActive,
            "Expected ROCm execution provider to be active — AMD GPU present but ROCm session did not initialize. Check ROCm driver installation.");

// AFTER
        Assert.True(engine.IsRocmSessionActive,
            "Expected MIGraphX execution provider to be active — AMD GPU present but MIGraphX session did not initialize. Check ROCm driver installation.");
```

### Change 6: Update RocmProviderTests

**File:** `InferenceEngine.Tests/RocmProviderTests.cs`

These tests validate the provider configuration logic. The test class name, test method names, and internal helper names reference "Rocm" which is still semantically correct (ROCm is the runtime, MIGraphX is the EP). However, the test at line 120 validates the "typed ROCm API" which no longer exists.

#### 6a. Update `ConfiguredSessionOptions_OnLinuxX64_UsesTypedRocmApi` (line 120)

Rename and update since there is no longer a "typed" API:

```csharp
// BEFORE
    public void ConfiguredSessionOptions_OnLinuxX64_UsesTypedRocmApi()

// AFTER
    public void ConfiguredSessionOptions_OnLinuxX64_UsesMIGraphXProvider()
```

Update the skip message inside (line 144-145):

```csharp
// BEFORE
                "ROCm provider was not appended on this Linux x64 environment; typed ROCm API configuration could not be validated.");

// AFTER
                "MIGraphX provider was not appended on this Linux x64 environment; MIGraphX configuration could not be validated.");
```

Update the other skip messages similarly (lines 56-57):

```csharp
// BEFORE
                "ROCm provider was not appended on this Linux x64 environment; ROCm configuration could not be validated.");

// AFTER
                "MIGraphX provider was not appended on this Linux x64 environment; MIGraphX configuration could not be validated.");
```

### Change 7: Update RestoreUpstreamCpuLibOnLinux Comment

**File:** `InferenceEngine.Core/InferenceEngine.Core.csproj` (lines 160-167)

```xml
  <!-- BEFORE -->
  <!-- The InferenceEngine.ROCm.Runtime.linux-x64 package ships a ROCm-enabled
       libonnxruntime.so that SIGSEGVs during its .init section on non-ROCm hosts.
       ...
       the provider .so (libonnxruntime_providers_rocm.so) is still
       copied; the runtime check in BaseInferenceEngine decides whether to dlopen it. -->

  <!-- AFTER -->
  <!-- The InferenceEngine.ROCm.Runtime.linux-x64 package ships a MIGraphX-enabled
       libonnxruntime.so that SIGSEGVs during its .init section on non-ROCm hosts.
       ...
       the provider .so (libonnxruntime_providers_migraphx.so) is still
       copied; the runtime check in BaseInferenceEngine decides whether to dlopen it. -->
```

**File:** `InferenceEngine.Tests/InferenceEngine.Tests.csproj` (lines 96-101)

```xml
  <!-- BEFORE -->
  <!-- Mirror the Core csproj target: the InferenceEngine.ROCm.Runtime.linux-x64 package
       (transitively pulled in by the Core project reference) ships a ROCm-enabled
       ...

  <!-- AFTER -->
  <!-- Mirror the Core csproj target: the InferenceEngine.ROCm.Runtime.linux-x64 package
       (transitively pulled in by the Core project reference) ships a MIGraphX-enabled
       ...
```

### Change 8: Update CI Workflow Test Filter

**File:** `.github/workflows/rocm-gpu-test.yml` (line 83)

No change required — the test filter uses `FullyQualifiedName~RocmProviderTests` which still matches the test class name (which we're keeping as `RocmProviderTests` since ROCm is the runtime platform).

---

## What NOT to Change

These items must remain as-is. Changing them will break things.

| Item | Location | Why |
|---|---|---|
| `IsRocmRuntimeUsable()` method | `BaseInferenceEngine.cs:369` | The probe checks `/dev/kfd` + `libhsa-runtime64.so` — MIGraphX still requires the ROCm userspace runtime. The method name and logic are correct. |
| `IsRocmSessionActive` property | `BaseInferenceEngine.cs:37` | The property name is semantically correct (ROCm = the hardware/runtime platform). Renaming to `IsMIGraphXSessionActive` would be a public API break for consumers. Keep as-is. |
| `nuget.config` GitHub Packages source | `nuget.config` | Already correctly configured with `github-intel-agency` source. No change needed. |
| `RestoreUpstreamCpuLibOnLinux` target logic | Both `.csproj` files | The `microsoft.ml.onnxruntime/1.24.1/...` path is already correct and matches both packages. No change needed. |
| `InferenceEngine.ROCm.Runtime.linux-x64` package name | `.csproj` PackageReference | The NuGet package name stays the same — only the version changes. The package contains ROCm-platform native libs regardless of which EP is used internally. |
| `_UpstreamCpuLib` path (1.24.1) | Both `.csproj` RestoreUpstreamCpuLibOnLinux targets | Already uses `1.24.1` — matches the new native package. No change needed. |
| `rocm-gpu-test.yml` test filter | CI workflow line 83 | `FullyQualifiedName~RocmProviderTests` still matches. |
| `RemoveWindowsNativeLibsOnNonWindows` target | Both `.csproj` files | No ROCm-specific references. Unchanged. |
| `InferenceEngineOptions.DeviceId` property | `InferenceEngineOptions.cs` | Still used by MIGraphX `device_id` option. Unchanged. |
| DirectML / CoreML EP paths | `BaseInferenceEngine.cs:250-276` | Unrelated to ROCm/MIGraphX migration. Do not touch. |
| Package tags `rocm` | `InferenceEngine.Core.csproj:18` | Still accurate — MIGraphX runs on the ROCm platform. |
| Class name `RocmProviderTests` | `InferenceEngine.Tests/RocmProviderTests.cs` | ROCm is the runtime platform. The class tests provider behavior on the ROCm platform. Renaming is optional but not required and would change test discovery. |

---

## Verification Checklist

After implementing all changes, verify:

- [ ] `dotnet build InferenceEngine.Core/ --runtime linux-x64` succeeds
- [ ] `dotnet test InferenceEngine.Tests/ --runtime linux-x64 --filter "FullyQualifiedName~RocmProviderTests"` passes (CPU fallback tests on non-ROCm hosts)
- [ ] `dotnet test InferenceEngine.Core.IntegrationTests/ --runtime linux-x64` passes (native lib validation)
- [ ] No compile errors from removed `OrtROCMProviderOptions` or `AppendExecutionProvider_ROCm` references
- [ ] Grep for `providers_rocm` returns zero hits in source files (only `_migraphx` should appear)
- [ ] Grep for `ROCmExecutionProvider` returns zero hits (only `MIGraphXExecutionProvider` should appear)
- [ ] The `InferenceEngine.ROCm.Runtime.linux-x64` package version in the `.csproj` is `1.24.1-dev.32` or later
