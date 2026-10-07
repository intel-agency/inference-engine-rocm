# Changelog

All notable changes to the **InferenceEngine.ROCm.Runtime.linux-x64** package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning 2.0](https://semver.org/spec/v2.0.0.html) via the `{ORT_VERSION}[-suffix].{run_number}` scheme.

> **License transition:** package versions `1.24.1.36` and earlier were licensed **AGPL-3.0-or-later**. The **MIT License** applies from the first public release onward (see [`LICENSE`](LICENSE) and [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt), both packed into the NuGet package root).

## [Unreleased]

### Added

- MIT-licensed package: `LICENSE` and `THIRD-PARTY-NOTICES.txt` ship inside the NuGet package
- Community files: `CHANGELOG.md`, `CONTRIBUTING.md`, `SECURITY.md`, GitHub issue and pull-request templates
- Planning documentation consolidated under `plan_docs/` (`.kilo/` planning notes migrated in)

### Changed

- Repository is now `.slnx`-only: legacy `inference-engine-rocm.sln` removed; the solution is `inference-engine-rocm.slnx`
- CI hardening: build attestation and split debug symbols *(in flight — later phases extend this entry)*
- Expanded validation test suite *(in flight — later phases extend this entry)*

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
