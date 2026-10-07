# Contributing to InferenceEngine.ROCm.Runtime.linux-x64

Thanks for your interest in contributing. This document covers how to build the native libraries from source, the project's branching and merge policy, and what we expect in a contribution.

**Please open an issue first** before starting significant work (new tests, CI changes, build-system changes, version bumps) so we can agree on the approach. Small documentation fixes don't need an issue.

## Building from source

The native libraries are compiled inside AMD's ROCm development container; you do not need a local ROCm install. Requirements: Docker (or a compatible runtime), and the .NET 10 SDK for packaging and tests.

```bash
# 1. Compile ONNX Runtime with the MIGraphX Execution Provider
#    (runs in rocm/dev-ubuntu-22.04:7.2.1, outputs to artifacts/)
docker run --rm -v "$(pwd)":/code -w /code rocm/dev-ubuntu-22.04:7.2.1 \
  /code/scripts/compile_onnx_rocm_docker.sh

# 2. Copy the built libraries into the pack staging area
cp artifacts/*.so InferenceEngine.Core/runtimes/linux-x64/native/

# 3. Build and pack
dotnet build inference-engine-rocm.slnx
dotnet pack InferenceEngine.Core/
```

The produced `.nupkg` lands in `InferenceEngine.Core/bin/Release/` (or `artifacts/` in CI).

## Version and couplet strategy

Each release pins a **couplet**: one ONNX Runtime source version plus one ROCm runtime version (current couplet: **ORT v1.24.1 + ROCm 7.2.1**). Package versions encode the ORT side (`{ORT_VERSION}[-suffix].{run_number}`). Cross-version support is planned via parallel package lines — see [`plan_docs/Multi-Version-Branching-Strategy.md`](plan_docs/Multi-Version-Branching-Strategy.md) before proposing version changes.

## Branching and merge policy

- Branch from **`development`**: `dev/<short-topic>`.
- Integration between long-lived branches (`development` → `staging` → `release`) happens via **merge commits only**. Squash merges and rebases are **banned** — they rewrite history and break the audit trail.
- **Every commit must be GPG-signed** (`git config commit.gpgsign true`). Unsigned commits are rejected.
- `master` mirrors the release line; `release` pushes produce continuous releases.

## Validation tiers

- **Tier 1** (required): the structural validation suite in `InferenceEngine.Core.IntegrationTests` — ELF header/symbol checks on the native libraries, ORT API loading, identity-model inference, and MIGraphX EP failure-path verification. It runs in CI on every build; locally it needs the CI-built `.so` files present in `InferenceEngine.Core/runtimes/linux-x64/native/`.
- **Tier 2** (planned): GPU hardware validation on a real ROCm host — full inference runs against MIGraphX-compiled models.

CI must be green before review, and reviews before merge. If your change affects the native build script, CI workflow, or packaging targets, call that out in the pull request — those areas are the most regression-prone.
