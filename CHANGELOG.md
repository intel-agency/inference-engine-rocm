# Changelog

All notable changes to the **InferenceEngine.ROCm.Runtime.linux-x64** package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning 2.0](https://semver.org/spec/v2.0.0.html) via the `{ORT_VERSION}[-suffix].{run_number}` scheme.

> **License transition:** package versions `1.24.1.36` and earlier were licensed **AGPL-3.0-or-later**. The **MIT License** applies from the first public release onward (see [`LICENSE`](LICENSE) and [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt), both packed into the NuGet package root).

## [Unreleased]

### Security

- **Supply-chain provenance disclosure:** versions `<= 1.24.1.44` were built from upstream inputs resolved by mutable tags (Docker `rocm/dev-ubuntu-22.04:7.2.1`, ORT tag `v1.24.1`). From `1.24.1.45` onward, builds are pinned by image digest and source commit and carry attestations for both the package and the native libraries. All released artifacts have been verified consistent with their CI builds (nuget.org inner content byte-identical to the GitHub release asset; `gh attestation verify` green; SHA256SUMS match).
- Native-build supply chain pinned (HIGH-1): ORT cloned at commit `470ae160` (tag-contains-SHA guard), Docker base image digest-pinned, native-lib attestations in addition to the nupkg attestation — and the pins actually govern the publish line (`development` → `staging` → `release`)
- Build script hardening (MED-1): PyPI `cmake` pinned by exact version + hashes (`--require-hashes`), apt/dpkg toolchain state logged for forensics
- Workflow hardening (Group A): in-repo CodeQL advanced setup (all protected branches + PRs), actionlint gate, Dependabot, CODEOWNERS + required code-owner review, secrets/vars env-mapped out of `run:` blocks, `persist-credentials: false` on container-feeding checkouts, job timeouts, concurrency-serialized artifact pruning
- nuget.org trusted publishing (OIDC) with classic-key fallback; the verify workflow is an environment-gated negative test (a green key-mint there would indicate policy misconfiguration)
- Branch/tag governance: `release` environment requires reviewers with self-review blocked; `refs/tags/v*` protected against deletion and force-update; `master` mirror branch retired (signed archive tag `archive/master-2026-10`)

### Added

- Tier-1.5 package-consumption suite (`InferenceEngine.Core.PackageTests`): restores the packed `.nupkg` from a local feed like a real consumer and verifies package content (native layout, build + buildTransitive targets, compatibility placeholder, legal files, nuspec metadata), native asset precedence (onnxruntime.dll alias is the ROCm ELF64, `libonnxruntime.so` byte-identical to the packaged artifact), couplet version, and end-to-end identity-model inference. Replaces the CI-only inline consumer smoke script; runs in the `validate-native` CI job
- MIT-licensed package: `LICENSE` and `THIRD-PARTY-NOTICES.txt` ship inside the NuGet package
- Community files: `CHANGELOG.md`, `CONTRIBUTING.md`, `SECURITY.md`, GitHub issue and pull-request templates
- Planning documentation consolidated under `plan_docs/` (`.kilo/` planning notes migrated in)
- Couplet-invariance gates in the Tier-1 validation suite: the loaded native OnnxRuntime version must match `EXPECTED_ORT_VERSION` (injected by CI, matching `ORT_TAG`; falls back to the managed assembly version locally), and `libonnxruntime_providers_migraphx.so` must declare DT_NEEDED `libmigraphx_c.so.3` (SONAME drift means the host-library requirement changed)
- Tier-2 GPU validation plan (`plan_docs/Tier2-GPU-Validation-Plan.md`): self-hosted `amd-gpu` runner spec, `gpu-validation.yml` sketch, CPU-vs-GPU tolerance checks (implementation deferred to Phase 9)

### Changed

- Repository is now `.slnx`-only: legacy `inference-engine-rocm.sln` removed; the solution is `inference-engine-rocm.slnx`
- CI/CD hardening: `create-release` now runs behind the `release` GitHub environment (approval gate) before publishing to NuGet.org; the publish step fails fast when the `NUGET_API_KEY` secret is missing; release tags are created on the exact release commit (`target_commitish`)
- SLSA build-provenance attestations generated for every `.nupkg` (verify with `gh attestation verify <file.nupkg> --repo intel-agency/inference-engine-rocm`)
- Split debug symbols: builds compile with `-g`, CI splits DWARF into per-Build-ID `.debug` files released as GitHub Release assets; shipped `.so` files are stripped of DWARF but keep `.symtab` (all assets covered by `SHA256SUMS.txt`)
- Actions bumps (SHA-pinned): `actions/checkout` v7.0.1, `actions/setup-dotnet` v6.0.0, `softprops/action-gh-release` v3.0.3
- Test dependency bumps: `Microsoft.NET.Test.Sdk` 17.12.0 → 18.10.1, `xunit.runner.visualstudio` 2.8.2 → 4.0.0 (`xunit` stays 2.9.3; `Microsoft.ML.OnnxRuntime` stays pinned to the couplet)
- README rewritten for public readiness: native-gap framing, quick start (typed `AppendExecutionProvider_MIGraphX(0)` + string API), corrected host requirements (ROCm 7.x userspace with MIGraphX runtime — `libmigraphx_c.so.3` + `libamdhip64.so.7`, not "any ROCm 5.x+"), couplet compatibility table, build-pipeline diagram, validation and provenance sections
- Repository metadata: GitHub description and topics populated (docs/metadata only — no package surface change)

### Fixed

- **Package installable by direct consumers.** `1.24.1.41` (and all internal predecessors) failed `dotnet add package` with "incompatible with all frameworks": no `lib/` placeholder existed, and the build-transitive targets never ran for direct references (NuGet imports `build/` for those). Fixed with the `lib/netstandard2.0/_._` placeholder, dual `build/` + `buildTransitive/` target packing, and an `onnxruntime.dll` pinvoke alias at the application directory (upstream's Linux pinvoke name never resolves `libonnxruntime.so`). A consumer-simulation gate now runs in CI against every packed nupkg.

## [1.24.1.36] - 2026-06-11

First release on the MIGraphX Execution Provider.

### Added

- MIGraphX EP migration: native libraries rebuilt from ONNX Runtime **v1.24.1** + **ROCm 7.2.1** (the ORT + ROCm couplet), replacing the earlier ROCm EP build
- 5-job CI pipeline: build → pack → validate → publish
- Tier-1 validation suite (`InferenceEngine.Core.IntegrationTests`): ELF/symbol checks, ORT API loading, identity-model inference, and MIGraphX EP failure-path verification

### Note — tag provenance

The GitHub Release tag `v1.24.1.36` points at a `master`-branch commit (`19432d0`), while the release assets were built from the release-branch commit (`2377128`) recorded in the package metadata. Tags correctly identify the release commit from the next release onward.

## [1.19.2.x] - 2026-04-05

Initial ROCm-EP-era internal builds (ORT v1.19.2 + ROCm EP, pinned to the `rocm/dev-ubuntu-22.04:6.0.2` image). Superseded by the MIGraphX migration in `1.24.1.36`.

### Added

- Docker-based ONNX Runtime ROCm build (`scripts/compile_onnx_rocm_docker.sh`)
- Initial CI build workflow and structural test suite

[Unreleased]: https://github.com/intel-agency/inference-engine-rocm/compare/v1.24.1.36...HEAD
[1.24.1.36]: https://github.com/intel-agency/inference-engine-rocm/releases/tag/v1.24.1.36
