# Inference Engine Library - Architecture & Build Guide

## 1. Executive Summary

This repository hosts **InferenceEngine.Core**, a cross-platform .NET 10.0 library designed to provide a unified interface for running AI models (specifically ONNX) with hardware acceleration on Windows, Linux, and macOS.

**Core Philosophy:** "Write Once, Run Accelerated Anywhere."
The library abstracts away the complexity of loading different Execution Providers (DirectML, CoreML, ROCm) so the consumer code looks identical on all platforms.

---

## 2. The "Native Gap" Challenge

While .NET is cross-platform, high-performance AI inference relies on **Native C++ Libraries** (`.dll`, `.so`, `.dylib`) to talk to GPU drivers.

* **Windows:** Microsoft provides `Microsoft.ML.OnnxRuntime.DirectML` via NuGet. This works out-of-the-box.
* **macOS:** Microsoft provides CoreML support in the base package. This works out-of-the-box.
* **Linux (AMD GPU):** **CRITICAL GAP.** Microsoft does *not* provide a NuGet package for ROCm (AMD GPU) support on Linux. The standard Linux package is CPU-only.

### The Solution: Hybrid Build Pipeline

To support Linux AMD GPUs, we implement a **Hybrid Build Strategy**:
1.  **Managed Code:** Built normally via `dotnet build`.
2.  **Native Assets (Linux):** Compiled from source using a specialized **Docker Container**.
3.  **Packaging:** The native assets are injected into the NuGet package during the CI process.

---

## 3. System Architecture

```mermaid
graph TD
    subgraph Consumer_App
        App[Agent / App] --> IE_Core[InferenceEngine.Core]
    end

    subgraph InferenceEngine_Core
        IE_Core --> BaseEngine[BaseInferenceEngine.cs]
        BaseEngine --> Options[InferenceEngineOptions]
        BaseEngine --> |Loads| ModelProvider[Model Providers]
    end

    subgraph Hardware_Acceleration
        BaseEngine -- Windows --> DML[DirectML (NuGet)]
        BaseEngine -- macOS --> CoreML[CoreML (Built-in)]
        BaseEngine -- Linux --> ROCm[Custom Native Libs]
    end

    subgraph Build_Pipeline
        Docker[ROCm Docker Image] --> |Compiles| NativeSo[libonnxruntime_providers_rocm.so]
        NativeSo --> |Injected Into| Nuget[.nupkg]
    end
```

---

## 4. Build System Details

### 4.1. Linux ROCm Compilation (Docker)
**Script:** `scripts/compile_onnx_rocm_docker.sh`
* **Environment:** Uses `rocm/dev-ubuntu-22.04` image to guarantee correct compiler (`hipcc`) and system dependencies (`libcholmod`, etc.).
* **Process:**
    1.  Clones specific ONNX Runtime tag (e.g., `v1.19.2`).
    2.  Compiles with `--use_rocm` and `--cmake_extra_defines CMAKE_HIP_ARCHITECTURES="gfx1030;gfx1031;gfx1100"`.
    3.  **Outputs:** `libonnxruntime.so` and `libonnxruntime_providers_rocm.so`.

### 4.2. CI/CD Workflow (`.github/workflows/build-rocm-linux.yml`)
1.  **Build Native:** Runs the Docker script to generate `.so` files.
2.  **Inject:** Copies `.so` files into `InferenceEngine.Core/runtimes/linux-x64/native/`.
3.  **Pack:** Runs `dotnet pack`. The `.csproj` is configured to include files in `runtimes/` as native assets.
4.  **Publish:** Pushes the NuGet package to the registry.

---

## 5. Runtime Logic (`BaseInferenceEngine.cs`)

The engine detects the OS at runtime and loads the appropriate provider. For Linux ROCm, it uses the **String API** because there are no compile-time types for ROCm in the standard NuGet package.

```csharp
if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    // Dynamically load the custom .so provider we compiled
    var rocmOptions = new Dictionary<string, string> { ... };
    options.AppendExecutionProvider("ROCmExecutionProvider", rocmOptions);
}
```
