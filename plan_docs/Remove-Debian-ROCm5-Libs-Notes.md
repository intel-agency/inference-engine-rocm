# Remove Debian ROCm 5.7 libraries (keep only /opt/rocm-7.1.0)

> **Status: deferred indefinitely (2026-08-11).** The ROCm 7.1 / HIP stack on the host
> works as-is and must not be disturbed by package removal. Current builds link only
> `libamdhip64.so.7` cleanly, so the hazard this plan removes is latent, not active.
> Keep this document only as a reference should the double-HIP SIGSEGV recur: the
> pin/swap/remove ordering in steps 2–4 and the rollback recipe are still valid
> (note: 4 of the 13 listed packages are no longer installed as of 2026-08-11).

## Goal

Remove the Debian trixie ROCm 5.7/6.x packages that were manually installed on 2026-06-11
during environment setup for the custom-built onnxruntime ROCm libs. They were never needed
(the custom `libonnxruntime.so` has no HIP/HSA NEEDED entries; the MIGraphX provider resolves
`libamdhip64.so.7` from `/opt/rocm`), and they cause the double-HIP-runtime SIGSEGV diagnosed
in `~/src/github/nam20485/amd_kernels/src/multiple_hip.md`.

## Verified context (do not re-litigate)

- System: Debian 13 trixie + `repo.radeon.com/rocm/apt/7.1` (pin 600 via `/etc/apt/preferences.d/repo-radeon-pin-600`).
- ROCm 7.1 (AMD repo): `rocm-core`, `hip-runtime-amd`, `hip-dev`, `comgr` 3.0, `hsa-rocr` 1.18, `migraphx`, `miopen-hip`, `rocblas` in `/opt/rocm-7.1.0`.
- Debian 5.7/6.x stack to remove: `libamdhip64-5` (libamdhip64.so.5.7.31921), `libamd-comgr2` (comgr.so.2.6.0), `libhsa-runtime64-1` 6.1.2, `libhsakmt1`, `rocm-smi`/`librocm-smi64-1`, `rocminfo` 6.1.2, `rocm-device-libs-17` + dev/doc packages.
- **Dependency traps (verified via apt simulations):**
  1. `hip-runtime-amd` hard-depends on `rocminfo`; the installed one is Debian's 6.1.2-2. Removing it directly cascades to the entire ROCm 7.1 stack. Must swap to AMD's `rocminfo` 1.0.0.70100 first (its deps are `hsa-rocr` etc. — no Debian libs).
  2. Debian `rocminfo` depends on `libhsa-runtime64-1`, so the swap must happen before removing the HSA libs.
  3. After `apt upgrade`, apt would reinstall Debian rocminfo 6.1.2 (500-pin beats installed 1.0.0 at 600 — version comparison). Requires a package-specific pin ≥1001.
- `hsa-rocr` 7.1 bundles the thunk statically (`libhsakmt.a`) and ships its own `libhsa-runtime64.so.1.18.70100` — Debian's HSA/thunk libs are redundant.
- Installed `hipcc` (AMD 1.1.1.70100) depends only on `rocm-core`, `rocm-llvm` — not on the Debian libs. The Debian-hipcc reverse-deps shown by `apt-cache rdepends` are for the uninstalled Debian variant.
- Manual AMD anchors keeping the auto-marked AMD chain alive: `hip-runtime-amd`, `migraphx`, `amdgpu-install`.
- Keep: Debian `clang-19`/`wasi-libc` (used for WASM work; after .so.5 removal its HIP link injection fails loudly instead of silently mis-linking).

## Steps

### 1. Snapshot (rollback reference)

```bash
dpkg -l | grep -iE "hip|rocm|hsa|comgr|migraphx|miopen|rocblas" > /tmp/rocm-packages-before.txt
apt-mark showauto > /tmp/apt-auto-before.txt
```

### 2. Pin rocminfo to the AMD repo (prevents apt upgrade re-installing Debian's)

Create `/etc/apt/preferences.d/rocminfo-amdgpu`:

```
Package: rocminfo
Pin: release o=repo.radeon.com
Pin-Priority: 1001
```

### 3. Swap rocminfo to the AMD version

```bash
sudo apt-get update
sudo apt-get install --allow-downgrades rocminfo=1.0.0.70100-20~22.04
```

Gate: output must show ONLY `rocminfo` downgraded. Abort otherwise.

### 4. Simulate, then remove the Debian packages

```bash
apt-get remove --simulate libamdhip64-5 libamdhip64-dev libamdhip64-doc \
  libamd-comgr2 libamd-comgr-dev libhiprtc-builtins5 \
  libhsa-runtime64-1 libhsa-runtime-dev libhsakmt1 libhsakmt-dev \
  rocm-device-libs-17 rocm-smi librocm-smi64-1
```

Gate: the "will be REMOVED" list must contain ONLY the 13 packages above.
If ANY AMD package appears (`hip-dev`, `hip-runtime-amd`, `migraphx*`, `miopen*`, `rocblas*`, `comgr`, `hsa-rocr`, `rocm-core`, `rocm-llvm`), STOP and re-plan.

Then run the same command without `--simulate` (with `sudo`).

### 5. Clean up auto-installed leftovers (gated)

```bash
sudo apt-get autoremove --purge --simulate
```

Gate: expected removal is `libllvm17t64` (and possibly old kernels already listed as unneeded).
If any AMD/ROCm package appears, do NOT run autoremove; instead purge only the known leftover:

```bash
sudo apt-get purge libllvm17t64
```

### 6. Refresh loader cache

```bash
sudo ldconfig
```

## Validation

1. `ldconfig -p | grep -E "libamdhip64|libamd_comgr|libhsa-runtime64|libhsakmt"` → only `/opt/rocm/lib` entries (`libamdhip64.so.7`, `libamd_comgr.so.3`, `libhsa-runtime64.so.1`).
2. `ls /usr/lib/x86_64-linux-gnu/ | grep -iE "amdhip|comgr|hsa|hsakmt"` → empty.
3. `dpkg -l | grep -iE "hip|rocm|hsa|comgr"` → only `repo.radeon.com` 7.1 packages remain; diff against `/tmp/rocm-packages-before.txt` shows only the 13 Debian packages (+rocminfo version change) gone.
4. `/opt/rocm/bin/rocminfo` runs and lists the GPU (AMD rocminfo installed correctly).
5. `apt-get upgrade --simulate` → rocminfo is NOT re-upgraded to 6.1.2-2 (pin works).
6. Provider load check: if a `libonnxruntime_providers_migraphx.so` is present locally (integration test bin dir or NuGet extract), run `ldd` on it → all HIP/comgr refs resolve from `/opt/rocm/lib`. (CI Docker builds are unaffected — they never used host packages.)
7. amd_kernels regression (separate repo, follow-up): wipe stale `cmake-build-debug/`, configure with `CMAKE_HIP_COMPILER=/opt/rocm/llvm/bin/clang++`, rebuild, then `readelf -d <binary> | grep amdhip64` must show exactly ONE entry (`libamdhip64.so.7`), and the binary runs without SIGSEGV.

## Rollback

```bash
sudo apt-get install --allow-downgrades rocminfo=6.1.2-2
sudo apt-get install libamdhip64-5=5.7.1-6+deb13u1 libamdhip64-dev=5.7.1-6+deb13u1 \
  libamdhip64-doc=5.7.1-6+deb13u1 libamd-comgr2=6.0+git20231212.4510c28+dfsg-3+b2 \
  libamd-comgr-dev=6.0+git20231212.4510c28+dfsg-3+b2 libhiprtc-builtins5=5.7.1-6+deb13u1 \
  libhsa-runtime64-1=6.1.2-3 libhsa-runtime-dev=6.1.2-3 libhsakmt1=6.2.4+ds-1 \
  libhsakmt-dev=6.2.4+ds-1 rocm-device-libs-17=6.0+git20231212.5a852ed-2 \
  rocm-smi=6.1.2-1 librocm-smi64-1=6.1.2-1
sudo rm /etc/apt/preferences.d/rocminfo-amdgpu && sudo ldconfig
```

## Out of scope / follow-ups

- amd_kernels CMake fix (Option A from `multiple_hip.md`: `-Wl,--as-needed` + pin `CMAKE_HIP_COMPILER`) — recommended even after this removal, as defense in depth; validation step 7 covers the check.
- Removing Debian `clang-19` — deferred; still used for WASM (`wasi-libc`).
- No changes to this repo's files; CI (`build-rocm-linux.yml`) builds inside Docker and is unaffected.
