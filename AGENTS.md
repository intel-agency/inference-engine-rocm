# Instructions for AI Agents (v3.0)

## 1. Repository Overview

This repo produces **InferenceEngine.ROCm.Runtime.linux-x64** — a native-only NuGet package containing ROCm-accelerated ONNX Runtime libraries for Linux x64 using AMD's MIGraphX Execution Provider. It fills the gap left by Microsoft's missing ROCm NuGet support.

### Structure

```text
InferenceEngine.Core/                            # Package project (native-only, no managed C# code)
  InferenceEngine.ROCm.Runtime.linux-x64.csproj  # NuGet packaging
  buildTransitive/                               # MSBuild targets for native asset precedence
  runtimes/linux-x64/native/                     # .so files (injected by CI)
InferenceEngine.Core.IntegrationTests/           # Tier-1 validation tests
scripts/
  compile_onnx_rocm_docker.sh                    # Docker-based ROCm compilation
plan_docs/                                       # Planning docs, migration guides, execution logs
.github/workflows/
  build-rocm-linux.yml                           # CI: build → pack → validate → publish
.github/skills/
  fix-rocm-build/                                # Agent skill: monitor/fix ROCm build workflow
inference-engine-rocm.slnx                       # .NET solution (XML solution format — .slnx only)
AGENTS.md / README.md / ARCHITECTURE.md          # Agent instructions and docs
CHANGELOG.md / CONTRIBUTING.md / SECURITY.md     # Community files
```

## 2. Build & Run Commands

### .NET (packaging only — no managed code to compile)

```bash
dotnet build inference-engine-rocm.slnx   # build the solution (builds packaging + tests)
dotnet pack InferenceEngine.Core/          # produce NuGet package (requires .so files in runtimes/)
```

### Linux ROCm Native Build (Docker)

```bash
# Run inside rocm/dev-ubuntu-22.04:7.2.1 container, outputs to /code/artifacts/
docker run --rm -v "$(pwd)":/code -w /code rocm/dev-ubuntu-22.04:7.2.1 \
  /code/scripts/compile_onnx_rocm_docker.sh

# Produces: libonnxruntime.so, libonnxruntime_providers_migraphx.so in artifacts/
# Copy to runtimes dir before packing:
cp artifacts/*.so InferenceEngine.Core/runtimes/linux-x64/native/
dotnet pack InferenceEngine.Core/
```

## 3. Dependencies

### .NET Packages (integration tests only)

- `Microsoft.ML.OnnxRuntime` 1.24.1 (test project references this directly)

## 4. Conventions

- Solution: `inference-engine-rocm.slnx` (XML solution format; no legacy `.sln`)
- Package target framework: `netstandard2.0` (native-only, broadest compatibility)
- Integration tests target: `net10.0`
- License: MIT (see `LICENSE` and `THIRD-PARTY-NOTICES.txt`, both packed into the NuGet package)
- Branching: `development` → `staging` → `release` (continuous release on push to `release`); `master` mirrors the release line
- Versioning: `{VERSION_PREFIX}-{suffix}.{run_number}` (SemVer 2.0, `VERSION_PREFIX` tracks ORT source version)
- Couplet versioning: each package release pins an **ORT + ROCm couplet** (e.g. ORT v1.24.1 + ROCm 7.2.1). See `plan_docs/Multi-Version-Branching-Strategy.md`
