# InferenceEngine.Core

A cross-platform .NET 10.0 library for AI inference via [ONNX Runtime](https://onnxruntime.ai/), with automatic hardware acceleration selection and a generic agent-friendly interface.

**Motto:** *Write Once, Run Accelerated Anywhere.*

## Features

- Unified `BaseInferenceEngine<TInput, TOutput>` abstraction over ONNX Runtime
- Automatic execution provider selection at runtime:
  - **Windows** → DirectML (GPU)
  - **macOS** → CoreML (GPU/ANE)
  - **Linux AMD GPU** → ROCm (custom-compiled native `.so`)
  - **Fallback** → CPU
- Built-in model download and registry support
- `ILogger` integration via `Microsoft.Extensions.Logging`
- NuGet-packaged with symbols (`.snupkg`)

## Requirements

| Component | Requirement |
| :--- | :--- |
| .NET SDK | 10.0+ |
| Python (tooling) | 3.14+ |
| ROCm (Linux GPU) | 5.x+ (via Docker build) |

## Getting Started

### Install

```bash
dotnet add package InferenceEngine.Core
```

### Use

```csharp
using InferenceEngine.Core;

// Derive from BaseInferenceEngine<TInput, TOutput> and implement:
//   protected override string ModelName => "my-model.onnx";
//   protected override TOutput Postprocess(IDisposableReadOnlyCollection<OrtValue> outputs, TInput input)
//   protected override IReadOnlyCollection<NamedOnnxValue> Preprocess(TInput input)
```

The engine automatically loads the best available execution provider for the current platform.

## Build

```bash
# .NET library
dotnet build
dotnet pack

# Python tooling & tests
uv sync
uv run pytest
```

### Linux ROCm Native Build

Building the ROCm execution provider requires an AMD GPU host and Docker:

```bash
docker run --rm -it \
  --device=/dev/kfd --device=/dev/dri \
  -v "$(pwd)":/code \
  rocm/dev-ubuntu-22.04 \
  bash /code/scripts/compile_onnx_rocm_docker.sh

# Copy artifacts before packing
cp artifacts/libonnxruntime*.so InferenceEngine.Core/runtimes/linux-x64/native/
dotnet pack InferenceEngine.Core/
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full hybrid build pipeline.

## Project Structure

```text
InferenceEngine.Core/          # .NET 10.0 C# library
  BaseInferenceEngine.cs       # Core abstract engine
  InferenceEngine.Core.csproj
scripts/
  compile_onnx_rocm_docker.sh  # Builds native ROCm .so inside Docker
tools/
  seda_bootstrap.py            # SEDA bootstrapper
  seda_packer.py               # SEDA artifact packer
tests/                         # pytest suite for Python tooling
pyproject.toml                 # Python project (uv)
inference-engine-rocm.sln      # .NET solution
```

## License

[AGPL-3.0-or-later](https://www.gnu.org/licenses/agpl-3.0.html) — Copyright 2026 Artificial Intelligence Agency.
