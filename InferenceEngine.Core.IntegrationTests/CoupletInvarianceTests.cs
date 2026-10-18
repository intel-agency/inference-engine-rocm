using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Xunit;

namespace InferenceEngine.Core.IntegrationTests
{
    /// <summary>
    /// Tier 1 couplet-invariance tests — run on any Linux runner (no GPU required).
    /// Guard the ORT + ROCm couplet contract: the managed Microsoft.ML.OnnxRuntime
    /// reference and the native build must move together (see
    /// plan_docs/Multi-Version-Branching-Strategy.md). These tests never load the
    /// MIGraphX provider .so — it is inspected on disk only.
    /// </summary>
    public class CoupletInvarianceTests
    {
        // ROCm 7.2.x generation; a change here means the couplet's minimum host ROCm
        // changed — update README compat table + this constant together.
        private const string ExpectedMigraphxSoname = "libmigraphx_c.so.3";

        // ------------------------------------------------------------------
        // Couplet version invariance (managed <-> native)
        // ------------------------------------------------------------------

        [Fact]
        public void OrtEnv_Version_MatchesExpectedCouplet()
        {
            // Native version reported by the loaded ORT library (via the managed API,
            // same load path as the other OrtEnv tests).
            var nativeVersion = OrtEnv.Instance().GetVersionString();

            // Expected version: CI injects EXPECTED_ORT_VERSION; local runs fall back
            // to the managed assembly version so the suite stays green without natives
            // built from a different couplet.
            var envExpected = Environment.GetEnvironmentVariable("EXPECTED_ORT_VERSION");
            var useManagedFallback = string.IsNullOrEmpty(envExpected);
            var expectedVersion = useManagedFallback
                ? typeof(OrtEnv).Assembly.GetName().Version!.ToString()
                : envExpected!;
            var expectedSource = useManagedFallback
                ? "managed Microsoft.ML.OnnxRuntime assembly version (EXPECTED_ORT_VERSION not set)"
                : "EXPECTED_ORT_VERSION environment variable";

            var nativeNormalized = NormalizeVersion(nativeVersion);
            var expectedNormalized = NormalizeVersion(expectedVersion);

            Assert.True(nativeNormalized == expectedNormalized,
                $"Loaded native OnnxRuntime version does not match the expected couplet version.\n" +
                $"  native:  {nativeVersion} (normalized {nativeNormalized})\n" +
                $"  expected: {expectedVersion} (normalized {expectedNormalized}) from {expectedSource}\n" +
                $"The managed Microsoft.ML.OnnxRuntime reference and the native ORT/ROCm build are a couplet " +
                $"and must move together — see plan_docs/Multi-Version-Branching-Strategy.md.");
        }

        /// <summary>
        /// Normalizes version strings for couplet comparison: tolerates a leading 'v'
        /// (ORT_TAG-style, e.g. "v1.24.1") and insignificant trailing ".0" segments
        /// (managed assembly 1.24.1.0 == native 1.24.1). At least Major.Minor is kept.
        /// </summary>
        private static string NormalizeVersion(string versionString)
        {
            var trimmed = versionString.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(trimmed, out var parsed))
            {
                Assert.Fail($"Cannot parse version string '{versionString}' (normalized input '{trimmed}').");
                return string.Empty; // unreachable
            }

            var components = new List<int> { parsed.Major, parsed.Minor };
            if (parsed.Build >= 0) components.Add(parsed.Build);
            if (parsed.Revision >= 0) components.Add(parsed.Revision);
            while (components.Count > 2 && components[^1] == 0)
                components.RemoveAt(components.Count - 1);
            return string.Join(".", components);
        }

        // ------------------------------------------------------------------
        // MIGraphX SONAME gate (Linux only, on-disk inspection only)
        // ------------------------------------------------------------------

        [Fact]
        [Trait("Category", "LinuxOnly")]
        public void MigraphXProvider_NeedsExpectedMigraphxSONAME()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

            // Reach the provider .so via NATIVE_LIBS_DIR discovery — NEVER copy it into
            // the test output dir. The on-disk checks never load it, keeping the suite
            // independent of the host's ROCm state.
            var path = Path.Combine(NativeLibraryValidationTests.GetNativeLibsDir(),
                "libonnxruntime_providers_migraphx.so");
            Assert.True(File.Exists(path), $"Missing: {path}");

            var output = NativeLibraryValidationTests.RunCommand("readelf", $"-d {path}");
            var neededEntries = output
                .Split('\n')
                .Where(line => line.Contains("NEEDED"))
                .ToList();

            Assert.True(neededEntries.Any(line => line.Contains($"[{ExpectedMigraphxSoname}]")),
                $"{path} does not declare DT_NEEDED {ExpectedMigraphxSoname}.\n" +
                $"A missing or changed SONAME means the couplet's host-library requirement changed " +
                $"(SONAME drift = the minimum host ROCm/MIGraphX runtime changed; update the README " +
                $"compat table and ExpectedMigraphxSoname together).\n" +
                $"Actual DT_NEEDED entries:\n{string.Join("\n", neededEntries)}");
        }
    }
}
