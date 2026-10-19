# Plan: Resolve All PR #2 Review Comments

## Summary

PR #2 has **8 unresolved review threads** from `gemini-code-assist`. This plan addresses each one with code changes, replies, and thread resolution via GraphQL.

---

## Review Comments & Resolution Plan

### 1. [MEDIUM] Nested markdown code blocks — `plan_docs/MIGraphX-Migration-Plan.md:265`
**Thread ID:** `PRRT_kwDOR6OQ2s6I9rmT`  
**Comment ID:** `PRRC_kwDOR6OQ2s7KoNTf`

**Issue:** Outer code block at line 241 uses 3 backticks but contains nested code blocks (also 3 backticks), breaking rendering.

**Fix:** Change the outer block from ` ``` ` to ` ```` ` (4 backticks) at lines 241 and 266.

**Files:**
- `plan_docs/MIGraphX-Migration-Plan.md` — change line 241 and 266 from triple to quadruple backticks

---

### 2. [MEDIUM] Stale ORT v1.19.2 references — `plan_docs/ROCm-Integration-Implementation-Plan.md:24`
**Thread ID:** `PRRT_kwDOR6OQ2s6I9rmU`  
**Comment ID:** `PRRC_kwDOR6OQ2s7KoNTg`

**Issue:** Line 25 references ONNX Runtime v1.19.2 and `rocm/dev-ubuntu-22.04:6.0.2`.

**Fix:** Add a note at the top of the section clarifying this describes the historical ROCm 6.0.2 setup, now superseded by the MIGraphX migration to ORT v1.24.1 + ROCm 7.2.1.

**Files:**
- `plan_docs/ROCm-Integration-Implementation-Plan.md` — add clarification note near line 25

---

### 3. [MEDIUM] Docker Image Pinning stale references — `plan_docs/ROCm-Integration-Implementation-Plan.md:222`
**Thread ID:** `PRRT_kwDOR6OQ2s6I9rmV`  
**Comment ID:** `PRRC_kwDOR6OQ2s7KoNTj`

**Issue:** Docker Image Pinning section still references `rocm/dev-ubuntu-22.04:6.0.2` and ORT v1.19.2.

**Fix:** Update the Docker Image Pinning section to reference the new target state (ORT v1.24.1 + ROCm 7.2.1) while noting the historical constraint.

**Files:**
- `plan_docs/ROCm-Integration-Implementation-Plan.md` — update line 223 area

---

### 4. [MEDIUM] Stale ORT v1.19.2 in extraction plan — `plan_docs/ROCm-Native-Package-Extraction-Plan.md:19`
**Thread ID:** `PRRT_kwDOR6OQ2s6I9rmW`  
**Comment ID:** `PRRC_kwDOR6OQ2s7KoNTl`

**Issue:** Table references ORT v1.19.2 + ROCm 6.0.2 as pinned versions for `compile_onnx_rocm_docker.sh`.

**Fix:** Update the table note to reflect ORT v1.24.1 + ROCm 7.2.1.

**Files:**
- `plan_docs/ROCm-Native-Package-Extraction-Plan.md` — update line 22

---

### 5. [CRITICAL] Missing `libonnxruntime_providers_shared.so` — `scripts/compile_onnx_rocm_docker.sh:137`
**Thread ID:** `PRRT_kwDOR6OQ2s6I-I70`  
**Comment ID:** `PRRC_kwDOR6OQ2s7Ko2T3`

**Issue:** The MIGraphX provider links against `libonnxruntime_providers_shared.so`. The buildTransitive targets file removes ALL native assets from `Microsoft.ML.OnnxRuntime` (including its version of this shared lib). Without shipping our own copy, runtime loading will fail.

**Fix:**
1. Add `cp build/Linux/Release/libonnxruntime_providers_shared.so /code/artifacts/` to compile script
2. Update workflow `build-rocm-linux.yml` to copy the shared lib in "Inject Native Assets" step
3. Update workflow "Inject ROCm native libs into test output" step

**Files:**
- `scripts/compile_onnx_rocm_docker.sh` — add cp line after line 137
- `.github/workflows/build-rocm-linux.yml` — add shared lib to inject step (~line 78) and test injection step (~line 142)

---

### 6. [HIGH] Test validation for shared lib — `NativeLibraryValidationTests.cs:106`
**Thread ID:** `PRRT_kwDOR6OQ2s6I-I76`  
**Comment ID:** `PRRC_kwDOR6OQ2s7Ko2UD`

**Issue:** Tests should validate `libonnxruntime_providers_shared.so` exists and is non-empty.

**Fix:**
1. Add shared lib check to `LibOnnxRuntimeProvidersMIGraphX_Exists` test
2. Add `libonnxruntime_providers_shared.so` to the `BothLibs_AreNonEmpty` loop

**Files:**
- `InferenceEngine.Core.IntegrationTests/NativeLibraryValidationTests.cs` — update lines 93-108

---

### 7. [MEDIUM] Eigen cached directory remote mismatch — `scripts/compile_onnx_rocm_docker.sh:120`
**Thread ID:** `PRRT_kwDOR6OQ2s6I-I79`  
**Comment ID:** `PRRC_kwDOR6OQ2s7Ko2UG`

**Issue:** Cached `.git` directory from old GitLab remote will fail with new GitHub commit hash.

**Fix:** Replace the Eigen clone logic with remote URL validation, directory recreation on mismatch, and `git fetch` before checkout.

**Files:**
- `scripts/compile_onnx_rocm_docker.sh` — replace lines 114-120 with the suggested robust logic

---

### 8. [MEDIUM] ROCm version validation — `scripts/compile_onnx_rocm_docker.sh:95`
**Thread ID:** `PRRT_kwDOR6OQ2s6I-I8A`  
**Comment ID:** `PRRC_kwDOR6OQ2s7Ko2UL`

**Issue:** Empty ROCm version components produce invalid `rocm_version.h`, wasting 30-60 min build time.

**Fix:** Add early validation check after line 93 (after `ROCM_VERSION_STRING` is set).

**Files:**
- `scripts/compile_onnx_rocm_docker.sh` — add validation block after line 93

---

## Execution Order

1. **Compile script changes** (comments 5, 7, 8) — all in `scripts/compile_onnx_rocm_docker.sh`
2. **Workflow changes** (comment 5) — `.github/workflows/build-rocm-linux.yml`
3. **Test changes** (comment 6) — `NativeLibraryValidationTests.cs`
4. **Doc changes** (comments 1, 2, 3, 4) — plan_docs files
5. **Commit and push** all changes
6. **Reply to each comment** with explanation
7. **Resolve each thread** via GraphQL `minimizeComment` / `resolveReviewThread` mutation
8. **Leave final summary comment** on the PR
9. **Verify** 0 unresolved threads remain
