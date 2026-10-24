using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Xunit;

namespace InferenceEngine.Core.PackageTests
{
    /// <summary>
    /// Package-content tests — inspect the packed .nupkg (a zip) on disk.
    /// These catch packing regressions the repo-internal Tier-1 suite cannot see:
    /// missing files in the package, bad nuspec metadata, or a broken
    /// build/buildTransitive layout.
    /// </summary>
    public class PackageContentTests
    {
        internal const string PackageId = "InferenceEngine.ROCm.Runtime.linux-x64";

        internal static string LocateNupkg()
        {
            var fromEnv = Environment.GetEnvironmentVariable("ROCM_NUPKG_PATH");
            if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv))
                return fromEnv;

            // Fallback: nupkg/ (local `dotnet pack -o nupkg`) or nupkg-local/
            // (CI download dir), beside the repo root.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                foreach (var feed in new[] { "nupkg-local", "nupkg" })
                {
                    var feedDir = Path.Combine(dir.FullName, feed);
                    var candidate = Directory.Exists(feedDir)
                        ? Directory.GetFiles(feedDir, $"{PackageId}*.nupkg").OrderByDescending(f => f).FirstOrDefault()
                        : null;
                    if (candidate is not null)
                        return candidate;
                }
                if (File.Exists(Path.Combine(dir.FullName, "inference-engine-rocm.slnx")))
                    break; // repo root reached; don't walk past it
                dir = dir.Parent;
            }

            throw new FileNotFoundException(
                "Cannot locate the packed nupkg. Set ROCM_NUPKG_PATH or pack into ./nupkg beside the repo root.");
        }

        private static long EntryLength(ZipArchive zip, string entryName)
        {
            var entry = zip.GetEntry(entryName)
                ?? throw new FileNotFoundException($"Entry missing from nupkg: {entryName}");
            return entry.Length;
        }

        [Fact]
        public void Nupkg_HasExpectedNativeLayout()
        {
            using var zip = ZipFile.OpenRead(LocateNupkg());
            var expectations = new (string entry, long minBytes)[]
            {
                ("runtimes/linux-x64/native/libonnxruntime.so", 1_000_000),
                ("runtimes/linux-x64/native/libonnxruntime_providers_migraphx.so", 100_000),
                ("runtimes/linux-x64/native/libonnxruntime_providers_shared.so", 10_000),
            };
            foreach (var (entry, minBytes) in expectations)
            {
                var length = EntryLength(zip, entry);
                Assert.True(length > minBytes, $"{entry}: expected > {minBytes} bytes, got {length}");
            }
        }

        [Fact]
        public void Nupkg_HasBuildTargetsInBothBuildAndBuildTransitive()
        {
            // NuGet runs build/ for direct references and buildTransitive/ only for
            // transitive ones — both must ship or one consumer shape gets no
            // asset precedence and no onnxruntime.dll alias.
            using var zip = ZipFile.OpenRead(LocateNupkg());
            Assert.NotNull(zip.GetEntry("build/InferenceEngine.ROCm.Runtime.linux-x64.targets"));
            Assert.NotNull(zip.GetEntry("buildTransitive/InferenceEngine.ROCm.Runtime.linux-x64.targets"));
        }

        [Fact]
        public void Nupkg_HasCompatibilityPlaceholderAndLegalFiles()
        {
            using var zip = ZipFile.OpenRead(LocateNupkg());
            // Empty _._ placeholder makes NuGet treat the native-only package as
            // compatible; without it restore fails with NU1202.
            Assert.NotNull(zip.GetEntry("lib/netstandard2.0/_._"));
            Assert.NotNull(zip.GetEntry("README.md"));
            Assert.NotNull(zip.GetEntry("LICENSE"));
            Assert.NotNull(zip.GetEntry("THIRD-PARTY-NOTICES.txt"));
        }

        [Fact]
        public void Nupkg_NuspecMetadataIsCorrect()
        {
            var nupkgPath = LocateNupkg();
            // Version encoded in the filename is the CI-computed couplet version.
            var fileVersion = Path.GetFileNameWithoutExtension(nupkgPath)
                .Substring(PackageId.Length + 1); // strip "<id>."

            using var zip = ZipFile.OpenRead(nupkgPath);
            var nuspecEntry = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            using var stream = nuspecEntry.Open();
            var nuspec = XDocument.Load(stream);
            // nuspec layout: <package><metadata><id/>...</metadata></package> — fields
            // live under <metadata>, not under the root.
            var metadataElement = nuspec.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
            var metadata = metadataElement.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);

            Assert.Equal(PackageId, metadata["id"]);
            Assert.Equal(fileVersion, metadata["version"]);
            Assert.Equal("MIT", metadata["license"]);

            // <repository url="..." type="git" /> carries its data in attributes
            var repository = metadataElement.Elements().Single(e => e.Name.LocalName == "repository");
            var url = (string?)repository.Attribute("url");
            var type = (string?)repository.Attribute("type");
            Assert.Contains("github.com/intel-agency/inference-engine-rocm", url);
            Assert.Equal("git", type);
        }

        /// <summary>SHA-256 of a nupkg entry — used by the consumer tests to
        /// prove the loaded native library is exactly the packaged artifact.</summary>
        internal static string HashNupkgEntry(string entryName)
        {
            using var zip = ZipFile.OpenRead(LocateNupkg());
            var entry = zip.GetEntry(entryName)
                ?? throw new FileNotFoundException($"Entry missing from nupkg: {entryName}");
            using var sha = SHA256.Create();
            using var stream = entry.Open();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
    }
}