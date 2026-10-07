# Plan: Tier-2 GPU Validation (MIGraphX Inference on Real Hardware)

**Date:** 2026-06-11
**Status:** Draft — implementation deferred to Phase 9 (P9-B3) of the public-readiness plan
**Owner:** maintainers
**Depends on:** Tier-1 suite (`InferenceEngine.Core.IntegrationTests`, runs in CI on every build)

---

## 1. Purpose

Tier-1 validation (`.github/workflows/build-rocm-linux.yml` → `validate-native`) is **structural and CPU-only**: ELF64 format, symbol exports, DT_NEEDED gates, managed-load, CPU identity-model inference, and the MIGraphX EP failure path (clean managed exception, never a SIGABRT). It runs on any GitHub-hosted `ubuntu-latest` runner because it never touches a GPU — the provider `.so` is inspected on disk only (the eager-dlopen hazard makes copying it into the test output dir unsafe on hosts without ROCm userspace).

Tier-1 cannot prove the one thing this package exists for: **that the MIGraphX Execution Provider actually executes a model on an AMD GPU**. Tier-2 closes that gap. It runs the shipped `.so` artifacts against real hardware and asserts:

1. A session with the MIGraphX EP can be created (EP registration + device discovery succeed).
2. A real model runs end-to-end on the GPU (not silently falling back to CPU).
3. GPU output agrees with CPU output within a documented tolerance.

A Tier-2 pass is a release-blocking signal for the `release` branch once implemented; until hardware is procured, Tier-1 remains the gate.

## 2. Self-Hosted Runner Specification

| Item | Requirement | Notes |
|---|---|---|
| GPU | AMD gfx1030 (RDNA2, e.g. RX 6800/6900) **or** gfx1100 (RDNA3, e.g. RX 7900) | Both among the five architectures the current build ships (`CMAKE_HIP_ARCHITECTURES="gfx1030;gfx1031;gfx1100;gfx1101;gfx1102"` in `scripts/compile_onnx_rocm_docker.sh`); one is sufficient, gfx1100 preferred |
| Host OS | Ubuntu 22.04 LTS | Matches the `rocm/dev-ubuntu-22.04:7.2.1` build container generation |
| ROCm userspace | ROCm 7.x matched to the couplet, **including the MIGraphX runtime** (`migraphx`, `libmigraphx_c.so.3`) | The host provides the DT_NEEDED SONAMEs the provider links against; this is exactly what Tier-1's SONAME gate previews |
| Runner software | `actions/runner` installed as a **systemd service** (`svc.sh install`), auto-start on boot | Unattended, survives reboots, journald logging |
| Labels | `self-hosted,linux,x64,amd-gpu` | Workflows target `runs-on: [self-hosted, linux, x64, amd-gpu]` so generic self-hosted runners never pick the job up |
| Disk | ≥ 50 GB free | Docker ROCm images are ~5 GB + artifacts; test downloads add a few GB |
| Maintenance | Weekly reboot window; `rocm-smi` health in the workflow catches a wedged GPU early | — |

## 3. Workflow Sketch — `.github/workflows/gpu-validation.yml`

**Trigger:** `workflow_dispatch` (manual) initially; move to `on: push: branches: [release]` + a nightly schedule once stable. Rationale in §5.

```yaml
name: Tier-2 GPU Validation

on:
  workflow_dispatch:        # manual initially; add push:[release] + schedule later
  workflow_call:            # so the release pipeline can compose it later

permissions:
  contents: read

jobs:
  gpu-validate:
    runs-on: [self-hosted, linux, x64, amd-gpu]
    timeout-minutes: 30
    steps:
      - name: GPU sanity check
        run: |
          rocm-smi --showproductname
          rocm-smi --showuse --showmemuse --showtemp || true   # informational
          # Fail fast if the GPU or ROCm userspace is wedged.
          test -c /dev/kfd && echo "kfd present"

      - name: MIGraphX runtime sanity
        run: |
          # The provider's DT_NEEDED entries must resolve on this host.
          ldconfig -p | grep libmigraphx_c.so.3

      - name: Checkout repository
        uses: actions/checkout@<SHA-pinned>   # match build-rocm-linux.yml pins

      - name: Download native artifacts (from the triggering build)
        uses: actions/download-artifact@<SHA-pinned>
        with:
          name: native-rocm-libs
          path: native-libs

      - name: Run Tier-2 GPU tests
        env:
          NATIVE_LIBS_DIR: ${{ github.workspace }}/native-libs
          EXPECTED_ORT_VERSION: "1.24.1"   # must match ORT_TAG (couplet invariant)
        run: |
          dotnet test InferenceEngine.Core.IntegrationTests/InferenceEngine.Core.IntegrationTests.csproj \
            --configuration Release --runtime linux-x64 \
            --filter "Category=Tier2GPU" \
            --logger "console;verbosity=normal"

      - name: Upload failure artifacts
        if: failure()                      # logs are the debugging payload
        uses: actions/upload-artifact@<SHA-pinned>
        with:
          name: gpu-validation-logs
          path: |
            **/TestResults/*.trx
            /var/log/rocm_smi.log           # if the host exposes it to the runner user
          retention-days: 14
```

### Test additions (in the Tier-1 project, gated by trait)

New tests carry `[Trait("Category", "Tier2GPU")]` so Tier-1 CI never selects them:

1. **`MIGraphXEP_CreatesSession_IdentityModel`** — `SessionOptions.AppendExecutionProvider_MIGraphX(0)` on `identity.onnx`; session creation must succeed (unlike the Tier-1 test, which expects a clean *failure* on GPU-less hosts).
2. **`MIGraphXEP_RunsRealModel_OnGPU`** — one small, committed real ONNX model (e.g. MNIST-8 or a tiny MobileNet, < 5 MB) with known input; run with the MIGraphX EP and assert it completes and reports GPU execution (EP metadata / session profiler).
3. **`CPU_vs_GPU_Allclose_WithinTolerance`** — run the same real model twice (CPU EP vs MIGraphX EP); outputs must be element-wise close with a documented tolerance: `rtol = 1e-3`, `atol = 1e-4` for fp32 (MIGraphX fusion legitimately reorders floating-point ops; anything tighter is flaky, anything looser hides real bugs). Record max observed abs/rel error in the TRX on pass — drift over time is an early warning.

## 4. Container / Device Passthrough Notes

Two deployment options, in order of preference:

**Option A — bare-metal runner (recommended).** The `actions/runner` service runs directly on the ROCm host. No passthrough needed; the test process sees `/dev/kfd`, `/dev/dri`, and the ROCm userspace naturally. This is the lowest-friction path — GPU-in-container on GitHub runners is a known pain (device cgroup rules, matching driver/userspace versions inside and outside the container, SELinux denials).

**Option B — containerized runner.** If isolation is required (e.g. the host serves other workloads), the runner container needs:

- Device passthrough: `--device /dev/kfd --device /dev/dri` (all `renderD*` nodes; `/dev/kfd` is the compute interface, `/dev/dri/*` the display/aux interface).
- Group membership: the runner user must be in the `video` and `render` groups (`--group-add video --group-add render`, or `user: { uid, gid, groups: [video, render] }` in compose) — without `render`, KFD open fails with EACCES on modern ROCm.
- ROCm userspace **inside** the container, matching the host kernel driver version (kfd version skew is the classic failure). The `rocm/dev-ubuntu-22.04:7.2.1` image used for builds is the natural base, plus `migraphx` runtime packages.
- Shared memory and `--ipc=host` for large allocations if the model needs it.

The runner must NOT be a Kubernetes pod with a default runtime; device plugins for AMD are workable but add a second layer of version-skew to debug. Start bare-metal.

## 5. Dispatch Strategy & Cost/Idle Considerations

- **Phase 1 (implementation):** `workflow_dispatch` only — a human decides when a GPU run is worth the machine's time. Zero risk of surprise load on the host.
- **Phase 2 (pre-release):** compose via `workflow_call` into the release path — `create-release` (or a new `gpu-validate` job in `build-rocm-linux.yml` gating it) so every `release`-branch push is GPU-validated before the NuGet publish. Requires the runner to be online during release windows.
- **Phase 3 (steady state):** add a nightly `schedule` run against `release` to catch host ROCm drift (host userspace upgrades silently breaking the shipped `.so` is the exact failure class Tier-2 exists for).
- **Cost/idle:** this is donated/self-hosted hardware, not billed per-minute, but idle cost is real: the machine runs 24/7 for ~2 min/day of useful work in phase 1. Mitigations: schedule runs during agreed windows; let the runner be `Offline` outside them (systemd `svc.sh stop/start` on a timer) once dispatch moves to scheduled runs; keep `workflow_dispatch` runs on-demand so the lead is never blocked waiting for a window. If the host is a shared dev box, prefer phase-1 manual dispatch indefinitely.

## 6. Security Notes

Self-hosted runners on **public** repositories are a standing risk: any workflow in the runner's scope can execute code on the host, and the host has GPU/device access.

- **Scope the runner to this repo only** — never an org-level runner on a public org.
- **Require approval for all outside-collaborator workflow runs** (repo Settings → Actions → "Require approval for all outside collaborators"). PRs from forks must not reach the GPU runner; the default `pull_request` trigger from forks runs in the fork's context, but `workflow_run` and label-triggered flows can — audit triggers when adding phase-3 scheduling.
- **Alternatively run the runner on a schedule** (boot → nightly run → shutdown) so the attack surface exists only in a narrow window.
- The runner service user should be a dedicated non-login account, member of `video`/`render` only (no docker group, no sudo), with the systemd unit hardened (`ProtectSystem=strict`, `ProtectHome=true`, `PrivateTmp=true` where feasible).
- Prefer secrets-free design: the GPU validation job needs only `contents: read` + artifact download; it requires no `GITHUB_TOKEN` write scopes, so a leaked runner context has nothing valuable to exfiltrate.

## 7. Acceptance Criteria (for Phase 9 implementation)

- [ ] Runner registered with labels `self-hosted,linux,x64,amd-gpu`, online, systemd-managed
- [ ] `gpu-validation.yml` manual dispatch green end-to-end on the runner
- [ ] Identity + real model run on MIGraphX EP; CPU-vs-GPU allclose within documented tolerance (`rtol 1e-3` / `atol 1e-4`, fp32)
- [ ] Failure path exercised once (e.g. `EXPECTED_ORT_VERSION` mismatch) — failure artifacts uploaded and inspectable
- [ ] Decision recorded: manual-only, or composed into the release gate
