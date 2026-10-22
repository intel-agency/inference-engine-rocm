using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;

namespace InferenceEngine.Core.PackageTests
{
    /// <summary>
    /// Consumer smoke tests — this test project IS the consumer: it restored the
    /// packed nupkg from a local feed and was built with the same targets any
    /// downstream `dotnet add package` user gets. These tests verify the
    /// consumption contract on the built output and at runtime:
    /// native asset precedence over Microsoft.ML.OnnxRuntime, the
    /// onnxruntime.dll pinvoke alias, couplet version, and real inference.
    /// (Regression gate for the 1.24.1.41 compat bug, now as checked-in tests.)
    /// </summary>
    public class ConsumerSmokeTests
    {
        private static string OutputDir => AppContext.BaseDirectory;

        [Fact]
        public void Consumer_OutputContainsFlattenedRocmLibraries()
        {
            // The package's targets flatten the three shipped .so files to the
            // app directory (ORT discovers providers next to the core lib).
            // libonnxruntime_providers_migraphx.so is unique to THIS package —
            // Microsoft.ML.OnnxRuntime never ships it — so its presence
            // fingerprints our assets in the consumer output.
            foreach (var name in new[]
                     {
                         "libonnxruntime.so",
                         "libonnxruntime_providers_shared.so",
                         "libonnxruntime_providers_migraphx.so",
                     })
            {
                var path = Path.Combine(OutputDir, name);
                Assert.True(File.Exists(path), $"Missing from consumer output: {path}");
            }
        }

        [Fact]
        public void Consumer_OnnxruntimeDllAliasIsOurElf64NotAPe()
        {
            // ORT's managed bindings pinvoke "onnxruntime.dll" on all platforms;
            // the package's targets alias that name to our ROCm ELF. If the alias
            // is missing or points at a Windows PE, a vanilla consumer dies with
            // DllNotFoundException — this is exactly the 1.24.1.41 bug.
            var path = Path.Combine(OutputDir, "onnxruntime.dll");
            Assert.True(File.Exists(path), $"Missing pinvoke alias: {path}");

            using var stream = File.OpenRead(path);
            var header = new byte[5];
            Assert.Equal(5, stream.Read(header, 0, 5));

            Assert.True(header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F',
                $"onnxruntime.dll is not an ELF image (magic: {Convert.ToHexString(header)}); " +
                "an MZ/PE header means the alias points at a Windows binary or was never written");
            Assert.Equal(2, header[4]); // EI_CLASS: ELFCLASS64
        }

        [Fact]
        public void Consumer_LibOnnxRuntimeIsExactlyThePackagedArtifact()
        {
            // Strongest precedence proof: byte-identical to the .so inside the
            // nupkg under test. If Microsoft's CPU-only libonnxruntime.so won the
            // copy race instead, this hash differs.
            var outputPath = Path.Combine(OutputDir, "libonnxruntime.so");
            Assert.True(File.Exists(outputPath), $"Missing from consumer output: {outputPath}");

            string outputHash;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(outputPath))
                outputHash = Convert.ToHexString(sha.ComputeHash(stream));

            var packagedHash = PackageContentTests.HashNupkgEntry("runtimes/linux-x64/native/libonnxruntime.so");
            Assert.Equal(packagedHash, outputHash);
        }

        [Fact]
        public void Consumer_NativeVersionMatchesCouplet()
        {
            var nativeVersion = OrtEnv.Instance().GetVersionString();

            var envExpected = Environment.GetEnvironmentVariable("EXPECTED_ORT_VERSION");
            var expected = string.IsNullOrEmpty(envExpected)
                ? typeof(OrtEnv).Assembly.GetName().Version!.ToString()
                : envExpected;

            Assert.True(nativeVersion.TrimStart('v', 'V').StartsWith(Normalize(expected)),
                $"Loaded native ORT {nativeVersion} does not match expected couplet {expected} — " +
                "the managed package and the native build must move together " +
                "(see plan_docs/Multi-Version-Branching-Strategy.md)");
        }

        private static string Normalize(string version)
        {
            var trimmed = version.Trim().TrimStart('v', 'V');
            var parts = trimmed.Split('.');
            // Compare Major.Minor.Build; tolerate managed assembly "1.24.1.0"
            return string.Join(".", parts.Take(3));
        }

        [Fact]
        public void Consumer_IdentityModelInfersCorrectly()
        {
            // End-to-end: the exact path a downstream user hits — managed ORT API
            // resolving our ROCm-built native, loading a model, running a tensor.
            var modelPath = Path.Combine(OutputDir, "Resources", "identity.onnx");
            Assert.True(File.Exists(modelPath), $"identity.onnx not found at: {modelPath}");

            using var opts = new SessionOptions();
            opts.AppendExecutionProvider_CPU();
            using var session = new InferenceSession(modelPath, opts);

            var inputName = session.InputMetadata.Keys.First();
            var inputTensor = new DenseTensor<float>(new float[] { 42.0f }, new[] { 1 });
            var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

            using var results = session.Run(inputs);
            var output = results.First().AsTensor<float>().ToArray();
            Assert.Equal(new[] { 42.0f }, output);
        }
    }
}