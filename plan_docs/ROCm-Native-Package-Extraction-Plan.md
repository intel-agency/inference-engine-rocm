# Plan: Extract ROCm Native Runtime into Standalone NuGet Package

**Date:** 2026-04-06
**Status:** Draft
**Repos:**
- `intel-agency/inference-engine-rocm` — becomes the standalone native package
- `intel-agency/inference-engine-lib` — becomes a consumer of that package

---

## 1. Problem Statement

Microsoft does not ship a NuGet package with ROCm-accelerated ONNX Runtime native libraries for Linux. Consumers who want AMD GPU inference on Linux must compile from source. We've already solved this compilation problem in `inference-engine-rocm`, but the solution is currently entangled with a full copy of the `InferenceEngine.Core` C# library — making it an unusable fork rather than a reusable community package.

## 2. Current State

### `inference-engine-rocm` today ships:
| Asset | Should stay? | Notes |
|---|---|---|
| `scripts/compile_onnx_rocm_docker.sh` | **Yes** | Core build script, pins ORT v1.19.2 + ROCm 6.0.2 |
| `.github/workflows/build-rocm-linux.yml` | **Yes** (modify) | 4-job pipeline: build → pack → validate → publish |
| `InferenceEngine.Core/*.cs` | **No** — remove | Full duplicate of inference-engine-lib's C# code |
| `InferenceEngine.Core/ModelProviders/*.cs` | **No** — remove | Same |
| `InferenceEngine.Core/InferenceEngine.Core.csproj` | **Replace** | Currently builds a managed library; replace with native-only package project |
| `InferenceEngine.Core.IntegrationTests/` | **Yes** (adapt) | `NativeLibraryValidationTests.cs` validates `.so` files — keep & refocus |
| `ARCHITECTURE.md` | **Yes** (update) | Good docs, update to reflect new package scope |
| Python tooling (`main.py`, `pyproject.toml`, SEDA) | **Evaluate** | Orthogonal to the native package; keep or move separately |

### `inference-engine-lib` today:
- Has `buildTransitive/InferenceEngine.Core.targets` to override ORT CPU natives with ROCm natives
- CI injects `.so` files directly into `runtimes/linux-x64/native/` during pack
- Ships the `.so` files embedded in the main `InferenceEngine.Core` NuGet package

## 3. Target Architecture

```
inference-engine-rocm (standalone repo)
├── publishes: InferenceEngine.ROCm.Runtime.linux-x64  (native-only NuGet)
│   └── runtimes/linux-x64/native/
│       ├── libonnxruntime.so
│       └── libonnxruntime_providers_rocm.so
│
inference-engine-lib (library repo)
├── InferenceEngine.Core.csproj
│   └── <PackageReference Include="InferenceEngine.ROCm.Runtime.linux-x64" ... />
│   └── (no more vendored .so files or CI injection of natives)
│
Any consumer app
└── dotnet add package InferenceEngine.ROCm.Runtime.linux-x64
    (works standalone, no InferenceEngine.Core required)
```

## 4. Implementation Phases

### Phase 1: Restructure `inference-engine-rocm` as a native-only package

**Goal:** Ship `InferenceEngine.ROCm.Runtime.linux-x64` containing only native `.so` files.

#### 1a. Create a new native-only `.csproj`

Replace the current managed library `.csproj` with a minimal packaging project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>InferenceEngine.ROCm.Runtime.linux-x64</PackageId>
    <Version>1.0.0-dev</Version>
    <Authors>intel-agency</Authors>
    <Description>ROCm-accelerated ONNX Runtime native libraries for Linux x64 (AMD GPU).
      Drop-in replacement for the CPU-only libonnxruntime.so shipped by Microsoft.ML.OnnxRuntime.</Description>
    <PackageTags>onnxruntime;rocm;amd;gpu;linux;native;inference</PackageTags>
    <PackageLicenseExpression>AGPL-3.0-or-later</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/intel-agency/inference-engine-rocm</RepositoryUrl>

    <!-- This project has no managed code -->
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <None Include="runtimes/linux-x64/native/*.so"
          Pack="true"
          PackagePath="runtimes/linux-x64/native/" />
  </ItemGroup>

  <!-- buildTransitive targets to override ORT CPU natives -->
  <ItemGroup>
    <None Include="buildTransitive/InferenceEngine.ROCm.Runtime.linux-x64.targets"
          Pack="true"
          PackagePath="buildTransitive/" />
  </ItemGroup>
</Project>
```

#### 1b. Remove all duplicated C# library code

Delete from `inference-engine-rocm`:
- `InferenceEngine.Core/BaseInferenceEngine.cs`
- `InferenceEngine.Core/IInferenceEngine.cs`
- `InferenceEngine.Core/InferenceEngineOptions.cs`
- `InferenceEngine.Core/ModelRegistry.cs`
- `InferenceEngine.Core/models.json`
- `InferenceEngine.Core/ModelProviders/` (entire directory)

#### 1c. Move the `buildTransitive` targets into this repo

Port `inference-engine-lib`'s `buildTransitive/InferenceEngine.Core.targets` here, renamed to `InferenceEngine.ROCm.Runtime.linux-x64.targets`. Update the `NuGetPackageId` conditions to match the new package name. This way, **any** consumer (not just `InferenceEngine.Core`) gets automatic native override behavior.

#### 1d. Adapt integration tests

`NativeLibraryValidationTests.cs` should validate:
- The `.so` files exist in the expected `runtimes/` path
- The `.so` files are ELF binaries with expected symbols
- The packed `.nupkg` contains the correct `runtimes/linux-x64/native/` entries

Remove any tests that depend on the managed `InferenceEngine.Core` C# classes.

---

### Phase 2: Update CI in `inference-engine-rocm`

#### 2a. Branching & environment model

Adopt a three-environment branch promotion scheme (matches your OdbDesign pattern):

| Branch | Environment | NuGet version format | Example |
| --- | --- | --- | --- |
| `development` | Dev | `{VERSION_PREFIX}-dev.{run_number}` | `1.19.2-dev.42` |
| `staging` | Staging | `{VERSION_PREFIX}-rc.{run_number}` | `1.19.2-rc.58` |
| `release` | Production | `{VERSION_PREFIX}.{run_number}` | `1.19.2.71` |

- `VERSION_PREFIX` is a **repository variable** (`${{ vars.VERSION_PREFIX }}`) set to the ORT source version (e.g., `1.19.2`). Updated manually when the Docker build script is bumped to a new ORT tag.
- The fourth position (or prerelease suffix) is always `github.run_number`, providing monotonically increasing build IDs.
- NuGet SemVer 2.0.0 sorts correctly: `-dev.*` < `-rc.*` < stable.

#### 2b. Refactor `build-rocm-linux.yml`

Single workflow triggered on push to all three branches:

```yaml
name: Build ROCm Native & Pack

on:
  workflow_dispatch:
  push:
    branches: [development, staging, release]
    paths:
      - "scripts/compile_onnx_rocm_docker.sh"
      - ".github/workflows/build-rocm-linux.yml"

env:
  DOTNET_VERSION: "10.0.x"
```

The **pack** job computes the version from the branch name:

```yaml
- name: Compute Package Version
  run: |
    PREFIX="${{ vars.VERSION_PREFIX }}"
    RUN="${{ github.run_number }}"
    case "${{ github.ref_name }}" in
      development) VERSION="${PREFIX}-dev.${RUN}" ;;
      staging)     VERSION="${PREFIX}-rc.${RUN}" ;;
      release)     VERSION="${PREFIX}.${RUN}" ;;
      *)           VERSION="${PREFIX}-local.${RUN}" ;;
    esac
    echo "VERSION=$VERSION" >> $GITHUB_ENV
    echo "Computed version: $VERSION"

- name: Pack NuGet Package
  run: |
    dotnet pack InferenceEngine.ROCm.Runtime.linux-x64.csproj \
      --configuration Release \
      --output ./nupkg \
      -p:Version=${{ env.VERSION }}
```

No `dotnet build` step needed — no managed code to compile.

#### 2c. Publish to GitHub Packages (all environments)

Every successful build publishes to GitHub Packages so `inference-engine-lib` can consume dev/rc builds:

```yaml
  publish-github-packages:
    name: Publish to GitHub Packages
    needs: validate-native
    runs-on: ubuntu-latest
    permissions:
      packages: write
    steps:
      - uses: actions/download-artifact@v4
        with: { name: nuget-package-rocm, path: ./nupkg }
      - run: |
          dotnet nuget push "./nupkg/*.nupkg" \
            --source "https://nuget.pkg.github.com/intel-agency/index.json" \
            --api-key ${{ secrets.GITHUB_TOKEN }} \
            --skip-duplicate
```

#### 2d. Production release job (release branch only)

On push to `release`, an additional job creates a GitHub Release with the `.nupkg`, raw `.so` files, and a SHA-256 checksum file as downloadable assets, then publishes to nuget.org:

```yaml
  create-release:
    name: Create GitHub Release & Publish to NuGet.org
    needs: validate-native
    if: github.ref_name == 'release'
    runs-on: ubuntu-latest
    permissions:
      contents: write
      packages: write
    steps:
      - uses: actions/download-artifact@v4
        with: { name: nuget-package-rocm, path: ./nupkg }
      - uses: actions/download-artifact@v4
        with: { name: native-rocm-libs, path: ./native-libs }

      - name: Generate checksums
        run: |
          cd native-libs
          sha256sum *.so > SHA256SUMS.txt
          cat SHA256SUMS.txt
          cd ../nupkg
          sha256sum *.nupkg >> ../native-libs/SHA256SUMS.txt

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          tag_name: v${{ env.VERSION }}
          name: "v${{ env.VERSION }}"
          generate_release_notes: true
          files: |
            native-libs/*.so
            native-libs/SHA256SUMS.txt
            nupkg/*.nupkg

      - name: Publish to NuGet.org
        run: |
          dotnet nuget push "./nupkg/*.nupkg" \
            --source "https://api.nuget.org/v3/index.json" \
            --api-key ${{ secrets.NUGET_API_KEY }} \
            --skip-duplicate
```

---

### Phase 3: Update `inference-engine-lib` to consume the package

#### 3a. Add PackageReference

In `InferenceEngine.Core/InferenceEngine.Core.csproj`:

```xml
<!-- ROCm native libraries for Linux x64 (AMD GPU acceleration) -->
<ItemGroup Condition="'$(RuntimeIdentifier)' == 'linux-x64' OR '$(RuntimeIdentifiers.Contains(linux-x64))' == 'true'">
  <PackageReference Include="InferenceEngine.ROCm.Runtime.linux-x64" Version="1.0.0" />
</ItemGroup>
```

#### 3b. Remove vendored assets and CI injection

- Delete `InferenceEngine.Core/runtimes/linux-x64/native/*.so` (vendored files)
- Delete `InferenceEngine.Core/buildTransitive/InferenceEngine.Core.targets` (now lives in the ROCm package)
- Remove the `.so` injection steps from `inference-engine-lib`'s CI workflows
- Remove the `<None Include="runtimes/linux-x64/native/*.so" ...>` item group from `.csproj`
- Remove the `buildTransitive` pack entry from `.csproj`

#### 3c. Update CI workflow

`build-test.yml` no longer needs to download/inject `.so` files. The NuGet restore handles everything automatically.

---

### Phase 4: Documentation & Community Prep

#### 4a. README for the new package

Write a consumer-focused README:

```markdown
# InferenceEngine.ROCm.Runtime.linux-x64

ROCm-accelerated ONNX Runtime native libraries for Linux x64.

## Install
dotnet add package InferenceEngine.ROCm.Runtime.linux-x64

## What it does
Replaces the CPU-only `libonnxruntime.so` from `Microsoft.ML.OnnxRuntime`
with a ROCm-enabled build, giving you AMD GPU acceleration on Linux.

## Requirements
- Linux x64 with AMD GPU
- ROCm 5.x+ drivers installed on the host
- .NET 8.0+ (any TFM — package is native-only)
```

#### 4b. Update `ARCHITECTURE.md`

Narrow scope to the native build pipeline only. Remove references to managed C# library code.

#### 4c. License review

Current license is `AGPL-3.0-or-later`. Since this is a native-only package containing compiled ONNX Runtime (MIT licensed) + ROCm provider, verify license compatibility. Consider whether AGPL is the right choice for maximum community adoption vs. MIT/Apache-2.0.

---

## 5. Package Naming Considerations

| Option | Pros | Cons |
|---|---|---|
| `InferenceEngine.ROCm.Runtime.linux-x64` | Clear provenance, matches our branding | Tied to InferenceEngine name |
| `Microsoft.ML.OnnxRuntime.ROCm.linux-x64` | Matches Microsoft's naming convention | May confuse users into thinking it's official Microsoft |
| `OnnxRuntime.ROCm.linux-x64` | Neutral, descriptive | Generic |

**Recommendation:** `InferenceEngine.ROCm.Runtime.linux-x64` — clearly ours, clearly what it does.

## 6. Versioning Strategy

NuGet packages use **SemVer 2.0.0** (not the .NET assembly 4-position format).

- `VERSION_PREFIX` is a GitHub **repository variable** set to the ORT source tag (e.g., `1.19.2`). Updated manually when the build script targets a new ORT release.
- The build number comes from `github.run_number`, providing a monotonically increasing identifier.
- Version computation is **branch-driven**:

| Branch | Format | Semantics |
| --- | --- | --- |
| `development` | `{PREFIX}-dev.{run}` | Unstable, latest from dev |
| `staging` | `{PREFIX}-rc.{run}` | Release candidate, integration-tested |
| `release` | `{PREFIX}.{run}` | Production-stable, published to nuget.org |

- NuGet sorts these correctly: `1.19.2-dev.42` < `1.19.2-rc.58` < `1.19.2.71`
- Consumers pin to stable versions; `inference-engine-lib` CI can test against `-rc.*` before promotion
- When bumping to a new ORT tag (e.g., `1.24.1`), update `vars.VERSION_PREFIX` — the next build on any branch automatically picks it up

## 7. Risk Assessment

| Risk | Mitigation |
|---|---|
| ORT version mismatch between native libs and `Microsoft.ML.OnnxRuntime` NuGet | Pin compatible version ranges; document in README |
| buildTransitive targets don't fire for all project types | Test with console apps, ASP.NET, and test projects |
| Large package size (~200MB+ for native libs) | Expected for native GPU libs; document this |
| AGPL license deters commercial adoption | Consider relicensing native-only package to MIT |
| ROCm driver version incompatibility on consumer machines | Document minimum ROCm driver version; test matrix |

## 8. Task Checklist

- [ ] **Phase 1:** Strip managed C# code from `inference-engine-rocm`
- [ ] **Phase 1:** Create native-only `.csproj` with new PackageId
- [ ] **Phase 1:** Port and rename `buildTransitive` targets
- [ ] **Phase 1:** Adapt integration tests for native-only validation
- [ ] **Phase 2:** Set `vars.VERSION_PREFIX` repo variable (e.g., `1.19.2`)
- [ ] **Phase 2:** Refactor workflow with branch-driven versioning (`dev` / `rc` / stable)
- [ ] **Phase 2:** Add GitHub Packages publish job (all branches)
- [ ] **Phase 2:** Add production release job (release branch only): GH Release + nuget.org + checksums
- [ ] **Phase 2:** Create `development` and `staging` branches
- [ ] **Phase 2:** Test full CI pipeline end-to-end on each branch
- [ ] **Phase 3:** Add `PackageReference` in `inference-engine-lib`
- [ ] **Phase 3:** Remove vendored `.so` files and injection CI steps
- [ ] **Phase 3:** Remove `buildTransitive` targets from `inference-engine-lib`
- [ ] **Phase 3:** Verify `inference-engine-lib` CI still passes (all platforms)
- [ ] **Phase 4:** Write consumer README
- [ ] **Phase 4:** Update ARCHITECTURE.md
- [ ] **Phase 4:** License review and decision
- [ ] **Phase 4:** First preview publish to nuget.org

## 9. Dependencies & Sequencing

```
Phase 1 ──→ Phase 2 ──→ First successful CI build with .nupkg artifact
                              │
                              ▼
                         Phase 3 (inference-engine-lib consumes the published package)
                              │
                              ▼
                         Phase 4 (docs, license, public publish)
```

Phases 1 & 2 can be done in a single PR to `inference-engine-rocm`. Phase 3 is a separate PR to `inference-engine-lib`. Phase 4 runs in parallel once Phase 2 produces a working package.
