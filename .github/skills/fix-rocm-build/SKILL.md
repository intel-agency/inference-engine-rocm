---
name: fix-rocm-build
description: >-
  Monitor the "Build Linux ROCm & Pack" GitHub Actions workflow, diagnose failures,
  fix them, push changes, and repeat until the full pipeline succeeds. Use when:
  the ROCm native compilation or NuGet packing workflow is failing; you need to
  iterate on build script or workflow fixes; you want hands-off fix-push-monitor loops
  for the ROCm build.
---

# Fix ROCm Build Workflow

Iteratively monitor, diagnose, fix, and re-trigger the **Build Linux ROCm & Pack**
workflow until every job succeeds.

## Workflow Context

**Workflow file:** `.github/workflows/build-rocm-linux.yml`
**Trigger:** push to `master`/`main`/`release` (paths: `scripts/compile_onnx_rocm_docker.sh`, the workflow file itself), or `workflow_dispatch`.

### What the workflow does

This CI pipeline builds the **Linux AMD ROCm native shared libraries** required for
hardware-accelerated ONNX Runtime inference on AMD GPUs. Microsoft does not ship a
NuGet package with ROCm support, so we compile `libonnxruntime.so` and
`libonnxruntime_providers_rocm.so` from source inside a Docker container and inject
them into the NuGet package.

#### Jobs

- **`build-rocm`** — Compile native `.so` files. Runs `scripts/compile_onnx_rocm_docker.sh` inside `rocm/dev-ubuntu-22.04` Docker image. Clones ONNX Runtime `v1.19.2`, compiles with `--use_rocm` targeting RDNA 2/3 architectures (`gfx1030`, `gfx1031`, `gfx1100`). Outputs uploaded as `native-rocm-libs` artifact.
- **`pack-nuget`** — Pack .NET NuGet with native assets. Downloads the `.so` artifacts, injects them into `InferenceEngine.Core/runtimes/linux-x64/native/`, then runs `dotnet pack` with version `1.0.0-rocm.<run_number>`. Uploads `.nupkg` as `nuget-package-rocm` artifact.

#### Build script: `scripts/compile_onnx_rocm_docker.sh`

Steps inside the container:

1. Configure git safe directory
2. Clone ONNX Runtime at pinned tag (`v1.19.2`)
3. Install build deps (`cmake`, `protobuf`, `python3`, etc.)
4. Set ROCm environment (`ROCM_PATH`, `HIP_PATH`)
5. Compile with `./build.sh --use_rocm --skip_tests --parallel`
6. Copy `libonnxruntime.so` and `libonnxruntime_providers_rocm.so` to `/code/artifacts/`

### Common failure modes

- **Disk space exhaustion** — the ONNX Runtime build is large; the workflow frees space first but runner limits can still be hit.
- **Docker image tag changes** — `rocm/dev-ubuntu-22.04:latest` may break; pin to a known digest or version if unstable.
- **ONNX Runtime build flag changes** — upstream tags may deprecate flags; check ORT release notes.
- **CMake/compiler version mismatches** — the base image may ship different tool versions than expected.
- **Artifact path mismatches** — if ORT changes its build output layout, the `cp` commands fail.
- **.NET SDK version mismatch** — `DOTNET_VERSION: "10.0.x"` must match available SDK releases.
- **NuGet pack errors** — missing files, bad csproj paths, or version string issues.

## Procedure

Use the MCP GitHub tools for all GitHub operations. Load them with a tool search for
`mcp_github_` before starting.

### Loop (repeat until all jobs pass)

1. **Check latest workflow run status**
   - Use `mcp_github_list_pull_requests` or the Actions API to find the latest run of `build-rocm-linux.yml` on the current branch.
   - If all jobs succeeded → **DONE**. Report success and exit.

2. **Retrieve failure logs**
   - For each failed job, fetch the run logs.
   - Identify the exact step and error message.

3. **Diagnose root cause**
   - Map the error to one of the common failure modes above, or diagnose a new one.
   - Check the relevant file(s): `scripts/compile_onnx_rocm_docker.sh`, `.github/workflows/build-rocm-linux.yml`, `InferenceEngine.Core/InferenceEngine.Core.csproj`.

4. **Apply fix**
   - Edit the minimal set of files needed to resolve the failure.
   - Do NOT refactor or add unrelated changes.

5. **Commit and push**
   - Commit with a message like: `fix(ci): <concise description of what was fixed>`
   - Push to the current branch to re-trigger the workflow.

6. **Monitor the new run**
   - Wait for the workflow to start, then poll its status.
   - Once the run completes, loop back to step 1.

### Guardrails

- **Max iterations:** 5. If the workflow still fails after 5 fix-push cycles, stop and present a summary of all attempted fixes and remaining failures for human review.
- **Never force-push** or rewrite history.
- **Never modify production source code** (`BaseInferenceEngine.cs`, etc.) unless the build failure is caused by a compile error in that code. Prefer fixing CI/script files.
- **Always read the file before editing it** to avoid blind patches.
- After the loop succeeds, provide a brief summary of all changes made across iterations.
