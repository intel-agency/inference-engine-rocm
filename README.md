# InferenceEngine.ROCm.Runtime.linux-x64

ROCm-accelerated ONNX Runtime native libraries for Linux x64 (AMD GPU).

**Drop-in replacement** for the CPU-only `libonnxruntime.so` shipped by [`Microsoft.ML.OnnxRuntime`](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime). Gives you AMD GPU acceleration on Linux without compiling from source.

## The Problem

Microsoft ships ONNX Runtime NuGet packages with GPU support for Windows (DirectML) and macOS (CoreML), but **not** for Linux AMD GPUs. The standard Linux package is CPU-only. To get ROCm acceleration, you have to compile ONNX Runtime from source yourself — a complex Docker build that takes 60+ minutes.

## The Solution

This package contains pre-compiled native libraries built from ONNX Runtime source with ROCm enabled. Install it and your .NET app automatically gets AMD GPU acceleration on Linux.

## Install

```bash
dotnet add package InferenceEngine.ROCm.Runtime.linux-x64
```

If you're using [`InferenceEngine.Core`](https://github.com/intel-agency/inference-engine-lib), it references this package automatically — no action needed.

## How It Works

The package ships:

- `libonnxruntime.so` — ROCm-enabled ONNX Runtime (replaces the CPU-only version)
- `libonnxruntime_providers_migraphx.so` — MIGraphX execution provider

A `buildTransitive` MSBuild targets file ensures these take precedence over the CPU-only natives from `Microsoft.ML.OnnxRuntime`.

## Requirements

| Component | Requirement |
| :--- | :--- |
| OS | Linux x64 |
| GPU | AMD (RDNA2+/CDNA recommended) |
| ROCm drivers | 5.x+ installed on host |
| .NET | 8.0+ (package is native-only, any TFM works) |

## Building From Source

The native libraries are compiled from ONNX Runtime source inside a Docker container:

```bash
docker run --rm -it \
  -v "$(pwd)":/code \
  -w /code \
  rocm/dev-ubuntu-22.04:7.2.1 \
  /code/scripts/compile_onnx_rocm_docker.sh
```

This compiles ONNX Runtime v1.24.1 with MIGraphX support, targeting gfx1030/gfx1031/gfx1100/gfx1101/gfx1102 GPU architectures. Output goes to `artifacts/`.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full build pipeline details.

## Project Structure

```text
InferenceEngine.Core/                            # Package project (native-only, no managed code)
  InferenceEngine.ROCm.Runtime.linux-x64.csproj  # NuGet packaging
  buildTransitive/                               # MSBuild targets for native asset precedence
  runtimes/linux-x64/native/                     # .so files (injected by CI)
InferenceEngine.Core.IntegrationTests/           # Tier-1 validation (ELF, symbols, ORT loading)
scripts/
  compile_onnx_rocm_docker.sh                    # Docker-based ROCm compilation
inference-engine-rocm.sln                        # .NET solution
```

## Versioning

Package versions track the ONNX Runtime source version:

| Branch | Format | Example |
| :--- | :--- | :--- |
| `development` | `{ORT_VERSION}-dev.{build}` | `1.24.1-dev.42` |
| `staging` | `{ORT_VERSION}-rc.{build}` | `1.24.1-rc.58` |
| `release` | `{ORT_VERSION}.{build}` | `1.24.1.71` |

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
|:---|:---|:---|:---|
| 1.24.1.x | 1.24.1 | 7.2.1 | MIGraphX |
| 1.19.2.x | 1.19.2 | 6.0.2 | ROCm (deprecated) |

## License

[AGPL-3.0-or-later](https://www.gnu.org/licenses/agpl-3.0.html) — Copyright 2026 Artificial Intelligence Agency.
