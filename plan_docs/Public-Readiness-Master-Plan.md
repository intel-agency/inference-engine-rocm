# Public-Readiness Master Plan

**Status:** APPROVED — owner sign-off 2026-10-07 (MIT confirmed; Q2–Q7 take recommended defaults unless overridden)
**Date:** 2026-10-07
**Goal:** Take `inference-engine-rocm` from "works internally" to "publicly discoverable, installable from nuget.org, and demonstrative of senior engineering" — without breaking the existing `development → staging → release` pipeline.

This plan consolidates the 2026-10-07 readiness audit with all reviewer feedback (comment-by-comment traceability in §2).

---

## 1. Current State Snapshot (audit findings)

| Area | State |
|---|---|
| Product | Native-only NuGet pkg `InferenceEngine.ROCm.Runtime.linux-x64`; MIGraphX EP build (ORT v1.24.1 + ROCm 7.2.1) on `release`; `master` (default branch) is stale ROCm EP era (ORT v1.19.2) |
| Pipeline | 5-job workflow: build → pack → validate → GitHub Packages → release+nuget.org. All green except nuget.org push (was missing `NUGET_API_KEY`) |
| nuget.org | Never published. ID `InferenceEngine.ROCm.Runtime.linux-x64` unclaimed/available. `NUGET_API_KEY` repo secret now exists ✓ (verified 2026-10-07) |
| GitHub Packages | 7 versions, feed visibility `internal` (not public) |
| GitHub Release | `v1.24.1.36` published with .so assets + SHA256SUMS + nupkg; tag points at master commit `19432d0` but assets built from release commit `2377128` (nuspec records correct commit) |
| Environments | `release` environment exists ✓ |
| License | csproj declares AGPL-3.0-or-later; **no LICENSE file in repo** (GitHub shows none); decision pending → recommend MIT (§3, D1) |
| Binaries | `.so`s unstripped (ELF symtab, BuildIDs present), no DWARF; provider needs `libmigraphx_c.so.3` + `libamdhip64.so.7` → host requires ROCm 7.x userspace w/ MIGraphX runtime (README currently says "drivers 5.x+" — wrong) |
| Tests | 13 Tier-1 validation tests, green in CI; no native/managed version-match test; coverage N/A (native-only product) |
| Versions behind | ORT 1.24.1 → **1.30.0** (latest with a matching managed NuGet package; no NuGet pkgs exist for 1.28.3/1.29.1); ROCm 7.2.1 → 7.2.4 (patch) / 7.14.1 (newest); xunit.runner.visualstudio 2.8.2 → 4.0.0; Test SDK 17.12.0 → 18.10.1; actions/checkout v4 → v7; setup-dotnet v4 → v6; action-gh-release v2.2.2 → v3 |
| Repo hygiene | Vestigial SEDA/Python sections in AGENTS.md + .gitignore, `.lscache` committed on master, `.kilo/plans` + `.github/skills` agent artifacts, both `.sln` and `.slnx` on release, no description/topics/badges/CHANGELOG/CONTRIBUTING/SECURITY |

---

## 2. Feedback Traceability

Every review comment maps to a task:

| Feedback | Disposition | Task |
|---|---|---|
| "lets do it" — couplet strategy + new ORT/ROCm couplet + SONAME note in README | Execute strategy; upgrade to ORT 1.30.0 + ROCm 7.2.4 | P8, P6-T2 |
| "update all package dependencies to latest current" | Bump test deps, Actions pins; ORT managed ref moves **only** with the couplet (native/managed must match) | P5-T2 |
| License: donate to FOSS, match upstream MIT? | **Recommend MIT** (rationale §3 D1); LICENSE + THIRD-PARTY-NOTICES | P2 |
| Package icon "later" | Deferred | P9-B2 |
| Debug symbols split/strip "sounds good" | Implement `-g` + objcopy split + stripped nupkg + `.debug` release assets | P4-T4 |
| Drop `PackageRequireLicenseAcceptance` "ok" | Remove | P2-T3 |
| Tag provenance fix "ok" | `target_commitish: github.sha`; historical tag documented, not moved | P4-T3 |
| Tests: "make me shine but humble brag" | Validation-strategy README section; factual, mechanism-led tone | P6-T1 |
| Coverage N/A + self-hosted runner plan "later phase" | Write `Tier2-GPU-Validation-Plan.md`; implement later | P5-T3, P9-B3 |
| API docs N/A "ok" | Link ARCHITECTURE.md + migration guide from README instead | P6-T1 |
| `OrtEnv.Version` couplet test "do it" | Add native/managed version-match test | P5-T1 |
| Prefix reservation "how? email draft" | Draft in Appendix A; send after first publish | P7-T4 |
| `NUGET_API_KEY` secret "done" | Verified present 2026-10-07 ✓ | P0 |
| Workflow hardening + "add the release environment" | Fail-fast, `environment: release` gate, attestation; environment already exists ✓ | P4-T1/T2/T5 |
| GitHub Packages public flip "after validation" | Deferred checklist | P9-B1 |
| README/cruft/metadata "add pls" | Full scope | P3, P6 |
| Attestations instead of cert signing "lets do that" | SLSA build provenance via `actions/attest-build-provenance` | P4-T5 |
| Commit signing "how to?" | How-to in Appendix B (user-side, one-time) | P9-B4 |
| nuget.org account/API key "done" | Verified ✓ | P0 |
| Merge release→master + LICENSE + hygiene + workflow "ok do it" | | P1, P2, P3, P4 |
| README rewrite + badges + About/topics + sln-vs-slnx "do it" | | P6 |
| master = mirror of release line "yes" | Confirmed decision D2 | P1 |
| Master plan doc before implementation | This document | — |
| "yes- MIT" | License locked | D1, P2 |
| "just make sure that all commits are signed" | Standing signing rule; config verified | D8, B4 |
| "work in a feature branch off development … banned anything but merge merges" | Git-flow policy: merge commits only, no direct master commits | D7, §4 mechanics |
| "do delegate whenever feasible" | Subagent delegation plan | D9, Execution model |
| "switch … to glm-5.3 for impl" + subagent-model question | Model guide for lead + subagents | Execution model |

---

## 3. Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| D1 | Package + repo license | **MIT** — owner-confirmed 2026-10-07 | Matches upstream ONNX Runtime (MIT) — the binaries you redistribute are MIT, so MIT-in-MIT is the cleanest story. Maximally permissive = maximum adoption, which is the whole point of plugging the Linux/AMD platform gap; enterprise consumers (the ones who need managed AMD-GPU inference most) avoid copyleft packages by policy. AGPL's network-copyleft would actively repel the audience this package exists for. MIT also signals benevolence without legal friction. Runner-up: Apache-2.0 (adds an explicit patent grant; heavier text, NOTICE obligations). For a redistribution-of-upstream-binaries package, MIT is the senior, conventional choice — it's what Microsoft itself uses for ORT. |
| D2 | `master` role | Mirror of the release line | Confirmed by owner. Default branch = what recruiters and `dotnet` users see; it must reflect the shipped product. After every release, merge `release` → `master`. |
| D3 | First nuget.org publish | Publish current validated 1.24.1 couplet (relabeled MIT) **before** the 1.30 upgrade | Registers the package ID, gets a live badge, and ships already-validated binaries. The 1.30 couplet then lands as a normal version bump. Version immutability means the first push must already carry final metadata (MIT, new README). |
| D4 | Provenance instead of paid signing | GitHub Artifact Attestations (SLSA) + signed tags + SHA256SUMS | Free, verifiable by anyone (`gh attestation verify`), stronger supply-chain story than an unchecked cert. NuGet author signing (paid OV/EV cert) skipped. |
| D5 | Solution file | `.slnx` only (pending Q3) | .NET 10 SDK supports it natively; repo already targets net10. `.sln` removed to end the dual-format drift. |
| D6 | Agent artifacts | Merge `.kilo/plans/*` into `plan_docs/`; keep `.github/skills/fix-rocm-build` | One home for planning docs; the skill is legitimate repo automation and reads as AI-augmented engineering rigor. (Pending Q5.) |
| D7 | Git flow for all work | Feature branches off `development`; integrate with **merge commits only** (`--no-ff`) — squash and rebase merges are banned repo-wide | Owner policy (2026-10-07). `master` never receives direct work commits; it only gets release→master mirror merges (D2). |
| D8 | Commit signing | **Every commit signed** — work environments must have `commit.gpgsign=true` + `user.signingkey` set (verified on the primary machine: key `B4797B08032A9C92`, `tag.gpgsign=true`) | Owner requirement (2026-10-07). Agents verify `%G?` = `G` on commits they author; GitHub-UI merge commits carry GitHub's own signature (expected, normal). |
| D9 | Execution model | Implementation delegated to subagents where feasible; lead + subagents run the glm-5.3 family | Owner directive (2026-10-07). Model split in "Execution model" below. |

---

## 4. Phases

Sequencing rule: **all content phases (P1–P6) land on `development` → `staging` → `release` before the first nuget.org push (P7)**, because the README is packed into the nupkg and nuget.org versions are immutable.

Work branch: `dev/public-readiness` off current `development` (which already contains the 3-file delta over `release`: CI-Build-Optimization plan, Eigen-cache simplification, test rename).

**Git mechanics (D7/D8):** all work happens on feature branches cut from `development`. Integration into `development` / `staging` / `release` uses merge commits only (`git merge --no-ff`); never squash, never rebase, never commit work directly to `master` (it receives release→master mirror merges only). Every commit must be signed — before committing, verify `git config commit.gpgsign` is `true`; after, verify `git log --format='%G?' -1` shows `G`.

**Delegation (D9):** P2+P3 (license/hygiene), P4 (workflow), P5 (tests), and P6 (README/docs) touch near-disjoint files and run concurrently as worker subagents on `dev/public-readiness`; P1 (branch consolidation) and the P7/P8 merge trains + gated publishes stay with the lead agent. Independent verification of each phase goes to a reviewer subagent before merge.

### Execution model (subagents)

Subagents run the model configured for their delegation profile (worker/scout/reviewer) in Delta settings — **not** automatically the lead session's model. Verified catalog (2026-10-07): `glm-5.3[effort=low|high|max]` and `glm-5.3-flash[effort=low|high|max]` (both default to effort=low). There is no `glm-5.3-non-thinking` model in the catalog — the fast/cheap equivalents are `glm-5.3-flash` and/or `effort=low`. Recommended split:

| Role | Model selector |
|---|---|
| Lead session (integration, merge trains, gated publishes) | `glm-5.3[effort=high]` |
| Worker — workflow/packaging (P4), tests (P5), couplet upgrade (P8) | `glm-5.3[effort=high]` |
| Worker — docs/hygiene/README (P2, P3, P6) | `glm-5.3-flash[effort=high]` |
| Scout — research (ORT 1.30 build contract P8-T2, Actions pin audit P4-T6) | `glm-5.3-flash[effort=high]` |
| Reviewer — independent phase verification | `glm-5.3[effort=high]` |

Either set these as the profile defaults in Delta settings (then spawns need no explicit model), or instruct the lead to pass the selectors explicitly per spawn.

### Phase 0 — Prerequisites ✅ (complete)
- [x] nuget.org account + API key created (owner)
- [x] `NUGET_API_KEY` repo secret added — verified via API 2026-10-07
- [x] `release` GitHub environment exists — verified via API
- [x] Package ID availability on nuget.org confirmed (HTTP 404 = unclaimed)

### Phase 1 — Branch consolidation (owner: agent)
- [ ] T1: Merge `origin/release` → `master`. Expected both-added conflicts: `plan_docs/MIGraphX-Migration-Plan.md`, `inference-engine-rocm.code-workspace` (possibly `plan_docs/ROCm-*`). Resolution policy: **release side wins** for build/code/docs-of-record; **master side wins** for `Multi-Version-Branching-Strategy.md` and workspace config (master-only or newer there).
- [ ] T2: Verify master tree afterwards: MIGraphX workflow, ORT v1.24.1 script, `.lscache` gone, README = MIGraphX version.
- [ ] T3: Create `dev/public-readiness` from `development` for all subsequent phases (base ≥ `f4e3069` — PR #4 landed the `.slnx` workspace default and the deferred Debian-ROCm-5.7 plan doc; no conflicts with this plan, and it partially pre-empts P3-T5).
- [ ] T4: Push master (no force; the T1 merge commit is master's only change — never direct work commits, per D7). Master pushes don't trigger the build workflow (branch filter excludes master) — intentional.

**Acceptance:** `git diff master origin/release` shows only master-only strategy/workspace files; `dotnet build` green on master.

### Phase 2 — Relicense to MIT + third-party notices (owner: agent; gated on Q1)
- [ ] T1: Add `LICENSE` — MIT, `Copyright (c) 2026 Artificial Intelligence Agency` (holder per Q6).
- [ ] T2: Add `THIRD-PARTY-NOTICES.txt`: ONNX Runtime (MIT, © Microsoft), Eigen (MPL-2.0), AMD MIGraphX (MIT, © Advanced Micro Devices), ROCm component libraries (per ROCm license page). Pack into nupkg root via csproj `None … Pack=true`.
- [ ] T3: csproj: `PackageLicenseExpression` → `MIT`; remove `PackageRequireLicenseAcceptance`; update `Copyright`.
- [ ] T4: Update license references in README, AGENTS.md, ARCHITECTURE.md.
- [ ] T5: Note in CHANGELOG (created in P3): 1.24.1.36 and earlier were AGPL-3.0-or-later; MIT applies from the first public release onward.

**Acceptance:** `dotnet pack` output unzips to a nuspec with `<license type="expression">MIT</license>`; GitHub repo page detects MIT; notices file inside nupkg.

### Phase 3 — Repo hygiene + community files (owner: agent)
- [ ] T1: `AGENTS.md`: delete Python/uv/pytest and SEDA sections; update structure, conventions (MIT, couplets, `.slnx`), build commands.
- [ ] T2: `.gitignore`: drop SEDA entries and the Python block (no Python remains); keep .NET/IDE/OS/artifacts.
- [ ] T3: Remove committed `.lscache` (already absent post-P1; keep `*.lscache` ignored).
- [ ] T4: Move `.kilo/plans/*.md` → `plan_docs/` (rename for clarity), delete `.kilo/`. Keep `.github/skills/fix-rocm-build/SKILL.md`. (Q5)
- [ ] T5: Delete `inference-engine-rocm.sln`; keep `.slnx`; fix references (AGENTS.md, tests' `FindRepoRoot` looks for `.sln` — update to `.slnx`!). ⚠️ `NativeLibraryValidationTests.FindRepoRoot` keys off `inference-engine-rocm.sln` — must change in lockstep or tests break locally.
- [ ] T6: Add `CHANGELOG.md` (Keep-a-Changelog style: 1.19.2 ROCm-EP era → 1.24.1 MIGraphX migration → license change → next: 1.30 couplet).
- [ ] T7: Add `CONTRIBUTING.md` (build-from-source, couplet strategy, branch flow, validation tiers).
- [ ] T8: Add `SECURITY.md` (private vulnerability reporting channel, supported-versions matrix = couplet table).
- [ ] T9: Add `.github/ISSUE_TEMPLATE/bug_report.md`, `config.yml`; optional PR template.

**Acceptance:** no `seda`/`pyproject`/`.lscache` references anywhere (`grep -ri`); `dotnet test` green with the `.slnx` root-finder change.

### Phase 4 — CI/CD hardening, provenance, debug symbols (owner: agent)
- [ ] T1: Fail-fast in `create-release`: map secret to env, `[ -z "$NUGET_API_KEY" ] && { echo '::error::NUGET_API_KEY secret is not configured'; exit 1; }` before push.
- [ ] T2: Attach `environment: release` to `create-release` job (environment already exists); recommend enabling required reviewer = nam20485 in repo settings so nuget.org pushes are human-approved (Q4).
- [ ] T3: Fix tag provenance: `softprops/action-gh-release` gets `target_commitish: ${{ github.sha }}`. Historical `v1.24.1.36` mismatch: documented in CHANGELOG; tag **not** moved (release assets stay resolvable).
- [ ] T4: Debug symbols pipeline:
  - compile script: add debug info to the Release build (`--cmake_extra_defines CMAKE_CXX_FLAGS_RELEASE="-O3 -DNDEBUG -g"` or equivalent) — verify build-time/disk impact on the runner (Free-Disk-Space step already present);
  - CI: per `.so`, `objcopy --only-keep-debug` → `debuginfo/<name>-<BuildID>.debug`; `objcopy --strip-debug` the shipped `.so` (keeps symtab → backtraces still work); pack stripped `.so`s;
  - release job: upload `.debug` files + existing SHA256SUMS as GitHub Release assets; document BuildID-matched debugging in ARCHITECTURE.md.
- [ ] T5: SLSA provenance: `permissions: id-token: write, attestations: write` + `actions/attest-build-provenance@v4` (latest v4.2.2, pin SHA) on `./nupkg/*.nupkg` in `pack-nuget`.
- [ ] T6: Actions version audit + bump (SHA-pinned, per existing convention): checkout v4→v7.0.1, setup-dotnet v4→v6.0.0, upload/download-artifact v4→current, softprops v2.2.2→v3.0.3.
- [ ] T7: Optional (Q4b): GPG-sign `SHA256SUMS.txt` in release job — requires exporting a private key into a CI secret; **default: skip** (attestations + signed tags cover provenance without putting a signing key in CI).

**Acceptance:** `workflow_dispatch` run on `dev/public-readiness` fully green; `gh attestation verify <nupkg> --owner intel-agency` passes; nupkg `.so`s stripped-with-symtab; `.debug` assets on the (GitHub-Packages-only) run's artifacts.

### Phase 5 — Tests + dependency updates (owner: agent)
- [ ] T1: Couple-invariant test: assert `OrtEnv.Instance().Version` matches expected native version. Expected value injected by CI as env var `EXPECTED_ORT_VERSION` (set from `ORT_TAG` in the compile script / workflow); falls back to the managed assembly version when unset so local runs stay green. Add a matching `readelf -d` SONAME assertion for the provider (`libmigraphx_c.so.N` recorded as a constant → catches ROCm SONAME drift at validation time; directly serves the P8 upgrade).
- [ ] T2: Dependency bumps: `Microsoft.NET.Test.Sdk` 17.12.0 → **18.10.1**; `xunit.runner.visualstudio` 2.8.2 → **4.0.0**; `xunit` stays **2.9.3** (latest v2 stable) or migrates to `xunit.v3` (Q2). `Microsoft.ML.OnnxRuntime` stays pinned to the couplet (1.24.1 now, 1.30.0 in P8) — never "latest" independently.
- [ ] T3: Write `plan_docs/Tier2-GPU-Validation-Plan.md` (implementation deferred to P9-B3): self-hosted runner spec (gfx1030/1100 host, Ubuntu 22.04, ROCm 7.x, `actions/runner` as systemd service, labels `self-hosted,linux,x64,amd-gpu`), `gpu-validation.yml` sketch (MIGraphX session on identity + real model, CPU-vs-GPU output tolerance check, `rocm-smi` sanity), container device passthrough (`/dev/kfd`, `/dev/dri`, `video` group), dispatch strategy (manual → on-release), cost/idle considerations.

**Acceptance:** 14+ tests green in `validate-native`; version-match test fails loudly if managed/native diverge (prove by temporarily setting a wrong env var in a dev run).

### Phase 6 — README rewrite + repo metadata (owner: agent, owner reviews)
- [ ] T1: Full README rewrite. Tone: **let the engineering speak** — factual, mechanism-led, zero marketing adjectives; depth itself is the flex. Sections:
  1. Title + one-line value statement + badges (CI, nuget version, license, downloads)
  2. **The Native Gap** — MS ships DirectML (Win), CoreML (macOS), CUDA (Linux-NVIDIA) via NuGet; Linux+AMD gets CPU-only. Cross-platform inference libs silently fall back to CPU on AMD/Linux. This package completes the big-3 managed GPU-inference platform story.
  3. Quick start — `dotnet add package`, C# MIGraphX EP snippet (typed `AppendExecutionProvider_MIGraphX(0)` + generic string API)
  4. How it works — `buildTransitive` asset-precedence mechanism (incl. the empty-package guard), why native-only + netstandard2.0
  5. **Host requirements** — corrected: ROCm **7.x userspace with MIGraphX runtime** (`libmigraphx_c.so.3`, `libamdhip64.so.7` — readelf-verified), kernel driver (amdgpu/KFD), gfx matrix (1030/1031/1100/1101/1102), CPU fallback behavior when absent
  6. Couplet compatibility table (package ↔ ORT ↔ ROCm ↔ EP ↔ required host libs) + ROCm-EP→MIGraphX migration notes (link `plan_docs/MIGraphX-Downstream-Migration-Guide.md`)
  7. Build pipeline — mermaid diagram of the 5-job CI, Docker cross-compilation, why Eigen is commit-pinned, `rocm_version.h` rewrite quirk
  8. Validation strategy — Tier-1 (no-GPU structural: ELF64, symbol exports, managed load, identity inference, clean-exception-not-SIGABRT + the eager-dlopen hazard that shapes the test layout), Tier-2 GPU (planned, link P5-T3 doc), couplet-invariant version test, SLSA attestation verify command
  9. Provenance & supply chain — signed tags, SHA256SUMS, artifact attestations, nuspec commit pinning, split debug info
  10. Versioning scheme + branch model summary
  11. Building from source; docs index (ARCHITECTURE.md, plan_docs)
  12. License (MIT) + third-party notices pointer
- [ ] T2: Repo metadata via `gh repo edit`: description ("Pre-built ROCm/MIGraphX-accelerated ONNX Runtime native libraries for Linux x64 — AMD GPU inference for .NET without source builds"), topics (`onnxruntime rocm migraphx amd-gpu dotnet nuget gpu-inference machine-learning onnx native-library linux`), leave website blank.
- [ ] T3: ARCHITECTURE.md refresh: link from README, add debug-info + attestation sections, correct host-requirements.
- [ ] T4: Sync `master` from the branch state so the README/metadata phase is reviewable on the default branch (after P7 publish, final mirror sync happens again).

**Acceptance:** owner sign-off on README; GitHub About populated; badges resolve (nuget badge activates after P7 — use `img.shields.io/nuget/v/...` which renders "none" gracefully pre-publish).

### Phase 7 — First nuget.org publish (owner: agent executes, owner approves gate)
- [ ] T1: PR `dev/public-readiness` → `development` (validate run: `1.24.1-dev.N`), → `staging` (`1.24.1-rc.N`), → `release` per existing convention.
- [ ] T2: **De-risk:** from the staging run, manually push the `rc` nupkg to nuget.org first, inspect rendering (README, license, metadata), then **unlist** it. (Unlisted ≠ deleted, but keeps the public list clean; the rc also permanently burns only a prerelease version.)
- [ ] T3: On `release` push: pipeline runs; `create-release` pauses at the `release` environment gate → owner approves → GitHub Release `v1.24.1.N` (tag on the correct sha) + nuget.org push of MIT `1.24.1.N`.
- [ ] T4: Verify: nuget.org listing live, `dotnet add package InferenceEngine.ROCm.Runtime.linux-x64` in a scratch project restores; CPU-load smoke (Tier-1-equivalent) on a clean Linux box; `gh attestation verify` on the downloaded nupkg.
- [ ] T5: Send prefix-reservation email (Appendix A) for `InferenceEngine.` (must be after first publish — reservation requires existing matching IDs).
- [ ] T6: Mirror sync `release` → `master`; push.

**Acceptance:** public nuget.org page with MIT + rendered README + attestation; badge in README goes live.

### Phase 8 — Couplet upgrade: ORT 1.30.0 + ROCm 7.2.4 (owner: agent + owner)
Why these versions: managed `Microsoft.ML.OnnxRuntime` on nuget.org tops out at **1.30.0** (GitHub releases v1.28.3/v1.29.1 have **no** managed package — a couplet needs both sides); ROCm **7.2.4** keeps the `libmigraphx_c.so.3`/`libamdhip64.so.7` SONAME generation of 7.2.1 → existing hosts keep working. ROCm 7.14.x evaluated as a *later* couplet (SONAME bump risk → would raise minimum host ROCm; document per the compat table).
- [ ] T1: Create maintenance branch `version/1.24.1-migraphx-rocm7.2.1` from `release` **before** the bump (executes Option A of `Multi-Version-Branching-Strategy.md`; mark that doc's status table implemented).
- [ ] T2: Research ORT v1.30.0 MIGraphX build contract: no `Dockerfile.migraphx` in the v1.30 tree — pull requirements from ORT CI configs/docs at that tag (`--use_migraphx` flag validity, ROCm version CI uses, Eigen commit pin in `cmake/external/eigen.cmake`).
- [ ] T3: Bump: `ORT_TAG=v1.30.0`, `EIGEN_COMMIT` (from T2), Docker image `rocm/dev-ubuntu-22.04:7.2.4` (verify `migraphx-dev` availability; `-complete` variant if needed), gfx arch list (evaluate adding RDNA4 `gfx1200/gfx1201` if ORT 1.30 + ROCm 7.2 support them), managed test ref → 1.30.0.
- [ ] T4: Repo variable `VERSION_PREFIX` → `1.30.0` (owner or agent via `gh variable set`).
- [ ] T5: Dev-branch build spike (development push → `1.30.0-dev.N`): watch for cmake/flag drift; `nm` gate (OrtGetApiBase + MIGraphX EP exports) and new SONAME gate from P5-T1 must pass; `readelf -d` output recorded into README compat table (incl. any `libmigraphx_c.so.N` change).
- [ ] T6: Docs: README/ARCHITECTURE/migration-guide compat tables + CHANGELOG entry; downstream `inference-engine-lib` coordination note (its pin moves 1.24.1 → 1.30.0).
- [ ] T7: Ship through staging → release (gated publish) → mirror master.

**Acceptance:** nuget.org lists `1.30.0.N`; both couplets documented; Tier-1 suite green against 1.30 natives.

### Phase 9 — Deferred backlog (explicitly later)
- [ ] B1: GitHub Packages `internal → public`: first delete noise versions while internal (`1.19.2-dev.28/31/32`, superseded `-dev`/`-rc`, and decide on AGPL-metadata `1.24.1.36` — recommend delete since public GH Packages versions are effectively permanent). Owner executes after P7 validation, per comment.
- [ ] B2: PackageIcon — needs a 128×128 design (owner input); then csproj `PackageIcon` + pack.
- [ ] B3: Tier-2 self-hosted GPU runner — implement `Tier2-GPU-Validation-Plan.md`.
- [x] B4: Commit signing — owner config already in place (`commit.gpgsign=true`, `tag.gpgsign=true`, key `B4797B08032A9C92`). Superseded by standing rule **D8**: every agent commit signed, `%G?` verified. Appendix B kept as reference for new environments and for registering the public key with GitHub so the web UI shows **Verified**.
- [ ] B5: Dependabot for GitHub Actions SHA pins + NuGet test deps (`.github/dependabot.yml`).
- [ ] B6: Optional: docs site (GitHub Pages + DocFX) — only if/when a managed API surface exists (downstream repo).

---

## 5. Risks & Mitigations

| # | Risk | Mitigation |
|---|---|---|
| R1 | ORT 1.30 MIGraphX build-flag drift (no Dockerfile.migraphx at that tag) | P8-T2 research + T5 dev spike before touching staging/release; 1.24.1 couplet remains published and maintained either way |
| R2 | ROCm SONAME drift breaks hosts | Conservative 7.2.4 first; P5-T1 SONAME gate test; compat table lists exact required host libs per package version |
| R3 | `-g` debug info blows up build time / runner disk | Measure in dev spike; keep nupkg stripped; `.debug` only as release assets; Free-Disk-Space step already exists; retention 5 days |
| R4 | nuget.org first-push surprises (metadata rendering, README size caps) | P7-T2: push+inspect+unlist an `rc` version before the real release |
| R5 | Version immutability locks in mistakes | All content phases (P1–P6) precede first push (D3); environment gate = human check |
| R6 | Test SDK 18 / runner.vs 4 breaking changes | net10.0 target is fine; run full suite in dev before staging; xunit stays 2.9.3 unless Q2 says otherwise |
| R7 | master↔release merge conflicts | P1-T1 resolution policy defined up front |
| R8 | Historical AGPL 1.24.1.36 artifacts confuse the MIT story | CHANGELOG + release-notes annotation; B1 deletes the internal GH Packages version before going public |

## 6. Open Questions (need owner answers before/at the noted phase)

- ~~Q1~~ **Answered 2026-10-07: MIT confirmed.** Q2–Q7 take the recommended option if unanswered when their phase starts.
- **Q2 (P5):** xunit — stay on 2.9.3 (latest v2 stable, zero risk) or migrate to `xunit.v3` (current generation, small migration)?
- **Q3 (P3):** `.slnx`-only OK (recommended), or keep both formats for older-VS contributors?
- **Q4 (P4):** Enable required reviewer (you) on the `release` environment so every nuget.org push needs manual approval? Recommended: yes.
- **Q5 (P3):** `.kilo/plans` → merge into `plan_docs/` (recommended), delete, or keep as-is?
- **Q6 (P2):** MIT copyright holder: "Artificial Intelligence Agency", "Nathan Miller", or both?
- **Q7 (P8):** Couplet target confirmed: ORT 1.30.0 + ROCm 7.2.4 conservative (recommended) — or go straight to ROCm 7.14.x and accept a higher host minimum?

## 7. Definition of Ready (final checklist)

- [ ] nuget.org: package live, MIT, README rendered, correct metadata, install smoke-tested
- [ ] `gh attestation verify` passes on the published nupkg
- [ ] GitHub: repo description + topics set; license detected; badges green
- [ ] `master` == release-line mirror; no SEDA/Python/lscache cruft anywhere
- [ ] LICENSE + THIRD-PARTY-NOTICES in repo and in the package
- [ ] Tag provenance fixed going forward; checksums + (split) debug info on releases
- [ ] Tier-1 suite incl. couplet-invariant + SONAME gates green
- [ ] 1.30.0 couplet published; 1.24.1 couplet on a maintenance branch; compat tables accurate
- [ ] CHANGELOG / CONTRIBUTING / SECURITY / issue templates present
- [ ] (Later backlog) GH Packages public, icon, Tier-2 GPU runner

---

## Appendix A — nuget.org prefix reservation email draft

> Send to **support@nuget.org** *after* the first package is published (P7-T5). Fill the bracketed fields.

```markdown
Subject: Package ID prefix reservation request — "InferenceEngine."

Hi NuGet.org team,

I would like to request a package ID prefix reservation for the prefix:

    InferenceEngine.

**Account:** [your nuget.org username]
**Project:** InferenceEngine — open-source managed GPU-inference infrastructure (MIT licensed)
**Repository:** https://github.com/intel-agency/inference-engine-rocm

**Existing package(s) matching the prefix:**
- InferenceEngine.ROCm.Runtime.linux-x64 — pre-built ROCm/MIGraphX-accelerated
  ONNX Runtime native libraries for Linux x64 (AMD GPU). Fills the gap left by
  the absence of an official Linux/AMD GPU package for Microsoft.ML.OnnxRuntime.

**Planned packages under this prefix:**
- InferenceEngine.ROCm.Runtime.linux-x64 (published)
- InferenceEngine.Core — cross-platform managed inference abstraction that
  selects the best execution provider per OS/GPU (https://github.com/intel-agency/inference-engine-lib)
- Additional runtime packages per platform couplet as the matrix grows
  (e.g. future ORT/ROCm version couplets, other accelerator runtimes)

**Justification:** All packages under this prefix belong to a single coherent
open-source project family with a shared namespace, published from public
repositories with SLSA build-provenance attestations, signed release tags, and
published SHA-256 checksums. Reserving the prefix prevents confusing
look-alike packages and gives consumers a trusted, consistent namespace.

Thank you for your consideration,
Nathan Miller
Artificial Intelligence Agency
[email on the nuget.org account]
```

## Appendix B — Commit signing how-to (owner-side, one time)

Your tag `v1.24.1.36` is already GPG-signed, so you have a key. Two options:

**Option 1 — reuse your GPG key:**
```bash
gpg --list-secret-keys --keyid-format=long        # find the sec line, e.g. rsa4096/ABC123...
git config --global user.signingkey ABC123XXXX     # the key id
git config --global commit.gpgsign true
git config --global tag.gpgsign true
```
Then upload the *public* key (`gpg --armor --export ABC123XXXX`) to GitHub → Settings → SSH and GPG keys, so commits/tags show the green **Verified** badge in the web UI (local verification already works; the badge needs the key registered on the account).

**Option 2 — SSH signing (simpler, no expiry/passphrase-agent friction):**
```bash
ssh-keygen -t ed25519 -C "nmiller217@gmail.com" -f ~/.ssh/id_ed25519_signing
# GitHub → Settings → SSH and GPG keys → New SSH key → Key type: Signing Key
#   (paste ~/.ssh/id_ed25519_signing.pub)
git config --global gpg.format ssh
git config --global user.signingkey ~/.ssh/id_ed25519_signing.pub
git config --global commit.gpgsign true
```

Notes:
- Headless/terminal prompts: ensure `GPG_TTY=$(tty)` is exported (Option 1) or use `gpg-agent`/`ssh-agent`.
- Squash/merge commits made through the GitHub web UI are signed by GitHub's own key (shown as "Verified" from GitHub) — your signature applies to commits you author locally.
- Delta/agent-authored commits in this worktree won't carry your signature unless this machine's git config has it; that's expected.
