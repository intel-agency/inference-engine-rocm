# Instructions for AI Agents (v2.0)

## 1. Repository Overview

This repo produces **InferenceEngine.ROCm.Runtime.linux-x64** — a native-only NuGet package containing ROCm-accelerated ONNX Runtime libraries for Linux x64. It fills the gap left by Microsoft's missing ROCm NuGet support.

### Structure

```text
InferenceEngine.Core/                            # Package project (native-only, no managed C# code)
  InferenceEngine.ROCm.Runtime.linux-x64.csproj  # NuGet packaging
  buildTransitive/                               # MSBuild targets for native asset precedence
  runtimes/linux-x64/native/                     # .so files (injected by CI)
InferenceEngine.Core.IntegrationTests/           # Tier-1 validation tests
scripts/
  compile_onnx_rocm_docker.sh                    # Docker-based ROCm compilation
.github/workflows/
  build-rocm-linux.yml                           # CI: build → pack → validate → publish
inference-engine-rocm.sln                        # .NET solution
```

## 2. Build & Run Commands

### .NET (packaging only — no managed code to compile)

```bash
dotnet build                        # build the solution (builds packaging project)
dotnet pack InferenceEngine.Core/   # produce NuGet package (requires .so files in runtimes/)
```

### Linux ROCm Native Build (Docker)

```bash
# Run inside rocm/dev-ubuntu-22.04:6.0.2 container, outputs to /code/artifacts/
docker run --rm -v "$(pwd)":/code -w /code rocm/dev-ubuntu-22.04:6.0.2 \
  /code/scripts/compile_onnx_rocm_docker.sh

# Produces: libonnxruntime.so, libonnxruntime_providers_rocm.so in artifacts/
# Copy to runtimes dir before packing:
cp artifacts/*.so InferenceEngine.Core/runtimes/linux-x64/native/
dotnet pack InferenceEngine.Core/
```

### Python (managed via uv — separate SEDA tooling)

```bash
uv sync                # install all dependencies (dev group included)
uv run pytest          # run the test suite
```

## 3. Dependencies

### .NET Packages (integration tests only)

- `Microsoft.ML.OnnxRuntime` 1.24.1 (test project references this directly)

### Python (dev)

- `pytest>=7.0.0`
- `pytest-cov>=4.0.0`
- `hypothesis>=6.0.0`

## 4. SEDA Extension Mandating

| Content | Extension | Type |
| :--- | :--- | :--- |
| Files only | `.seda` | 0 |
| Files + Message | `.commit.seda` | 5 |
| Files + Commands | `.construct.seda` | 1 |
| Validated Patch | `.smartpatch.seda` | 1+5 |
| Encrypted | `.vault.seda` | 2 |
| Polyglot Web | `.seda.html` | 3 |

## 5. Conventions

- Package target framework: `netstandard2.0` (native-only, broadest compatibility)
- Integration tests target: `net10.0`
- `requires-python = ">=3.14"` (for Python tooling)
- License: AGPL-3.0-or-later
- Branching: `development` → `staging` → `release` (continuous release on push to `release`)
- Versioning: `{VERSION_PREFIX}-{suffix}.{run_number}` (SemVer 2.0, `VERSION_PREFIX` tracks ORT source version)
- Python dependencies are declared in `pyproject.toml` only
- pytest markers in use: `slow`, `integration`, `unit`
