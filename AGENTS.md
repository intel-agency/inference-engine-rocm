# Instructions for AI Agents (v1.5)

## 1. Repository Overview

This repo contains **InferenceEngine.Core** — a cross-platform .NET 10.0 library for AI inference via ONNX Runtime with hardware acceleration (DirectML on Windows, CoreML on macOS, ROCm on Linux AMD GPU) — plus Python tooling for SEDA artifact packaging and a test suite.

### Structure

```text
InferenceEngine.Core/   # .NET 10.0 C# library (primary deliverable)
scripts/                # compile_onnx_rocm_docker.sh — builds native ROCm .so files
tools/                  # seda_bootstrap.py, seda_packer.py — SEDA packaging utilities
tests/                  # pytest test suite for Python tooling
main.py                 # Python entry point (minimal)
pyproject.toml          # Python project config (uv managed)
pytest.ini              # pytest configuration
inference-engine-rocm.sln  # .NET solution file
```

## 2. Build & Run Commands

### .NET

```bash
dotnet build                        # build the solution
dotnet build InferenceEngine.Core/  # build just the library
dotnet pack InferenceEngine.Core/   # produce NuGet package
```

### Python (managed via uv)

```bash
uv sync                # install all dependencies (dev group included)
uv run pytest          # run the test suite
uv run python main.py  # run the entry point
```

### Linux ROCm Native Build (Docker)

```bash
# Run inside rocm/dev-ubuntu-22.04 container, outputs to /code/artifacts/
bash scripts/compile_onnx_rocm_docker.sh
# Produces: libonnxruntime.so, libonnxruntime_providers_rocm.so
# Copy artifacts to InferenceEngine.Core/runtimes/linux-x64/native/ before packing
```

## 3. Dependencies

### .NET Packages

- `Microsoft.ML.OnnxRuntime` 1.24.1 (base, CPU)
- `Microsoft.ML.OnnxRuntime.DirectML` 1.23.0 (Windows only)
- `Microsoft.ML.OnnxRuntime.Extensions` 0.14.0
- `Microsoft.Extensions.Logging.Abstractions` 10.0.3

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

- Target framework: `net10.0`, `requires-python = ">=3.14"`
- Nullable reference types and implicit usings enabled in C#
- Supported runtime identifiers: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`
- License: AGPL-3.0-or-later
- Python dependencies are declared in `pyproject.toml` only (`requirements-dev.txt` has been removed)
- pytest markers in use: `slow`, `integration`, `unit`
