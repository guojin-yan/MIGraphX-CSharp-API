using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using JYPPX.ROCm.MIGraphXSharp.Loading;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class M7RuntimeClosureTests
{
    [Fact]
    public void PackageMarkerVerifiesAllFilesAndFamilyIdentity()
    {
        using var fixture = MarkerFixture.Create();

        var result = RuntimeClosureGuard.ValidateCandidate(fixture.RootPath, requirePackageMarker: true);

        Assert.True(result.IsPackageCandidate);
        Assert.True(result.Success, result.Detail);
        Assert.StartsWith(RuntimeClosureGuard.RuntimeFamily + ":7.2.1:", result.Identity, StringComparison.Ordinal);
        Assert.Contains("libmigraphx_c.so.3", result.Detail, StringComparison.Ordinal);
        Assert.Contains("2 allowlisted files", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageMarkerRejectsMissingMarkerTamperingAndTraversal()
    {
        using var fixture = MarkerFixture.Create();
        File.Delete(fixture.MarkerPath);

        var missing = RuntimeClosureGuard.ValidateCandidate(fixture.RootPath, requirePackageMarker: true);
        Assert.False(missing.Success);
        Assert.Contains("missing migraphx-runtime-closure.xml", missing.Detail, StringComparison.OrdinalIgnoreCase);

        fixture.WriteMarker();
        File.AppendAllText(fixture.DependencyPath, "tampered", Encoding.UTF8);
        var tampered = RuntimeClosureGuard.ValidateCandidate(fixture.RootPath, requirePackageMarker: true);
        Assert.False(tampered.Success);
        Assert.Contains("hash changed", tampered.Detail, StringComparison.OrdinalIgnoreCase);

        fixture.WriteMarker(includeTraversal: true);
        var traversal = RuntimeClosureGuard.ValidateCandidate(fixture.RootPath, requirePackageMarker: true);
        Assert.False(traversal.Success);
        Assert.Contains("traversal-free", traversal.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyExplicitOrSystemCandidateDoesNotRequirePackageMarker()
    {
        var directory = Path.Combine(Path.GetTempPath(), "migraphx-m7-legacy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "libmigraphx_c.so.3");
        File.WriteAllText(path, "legacy", Encoding.UTF8);
        try
        {
            var result = RuntimeClosureGuard.ValidateCandidate(path, requirePackageMarker: false);

            Assert.True(result.Success, result.Detail);
            Assert.False(result.IsPackageCandidate);
            Assert.Null(result.Identity);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class MarkerFixture : IDisposable
    {
        private MarkerFixture(string directory)
        {
            NativeDirectory = Path.Combine(directory, "linux-x64", "native");
            var libraryDirectory = Path.Combine(NativeDirectory, "lib");
            Directory.CreateDirectory(libraryDirectory);
            RootPath = Path.Combine(libraryDirectory, "libmigraphx_c.so.3");
            DependencyPath = Path.Combine(libraryDirectory, "libdependency.so.1");
            MarkerPath = Path.Combine(NativeDirectory, RuntimeClosureGuard.MarkerFileName);
            File.WriteAllText(RootPath, "migraphx", Encoding.UTF8);
            File.WriteAllText(DependencyPath, "dependency", Encoding.UTF8);
        }

        internal string NativeDirectory { get; }
        internal string RootPath { get; }
        internal string DependencyPath { get; }
        internal string MarkerPath { get; }

        internal static MarkerFixture Create()
        {
            var fixture = new MarkerFixture(Path.Combine(Path.GetTempPath(), "migraphx-m7", Guid.NewGuid().ToString("N")));
            fixture.WriteMarker();
            return fixture;
        }

        internal void WriteMarker(bool includeTraversal = false)
        {
            var rootHash = Sha256(RootPath);
            var dependencyHash = Sha256(DependencyPath);
            var dependencyPath = includeTraversal ? "lib/../escape.so" : "lib/libdependency.so.1";
            var document = new XDocument(
                new XElement("runtimeClosure",
                    new XAttribute("schemaVersion", "1"),
                    new XAttribute("packageId", RuntimeClosureGuard.RuntimePackageId),
                    new XAttribute("packageVersion", RuntimeClosureGuard.RuntimePackageVersion),
                    new XAttribute("rid", "linux-x64"),
                    new XAttribute("family", RuntimeClosureGuard.RuntimeFamily),
                    new XAttribute("manifestContentDigestSha256", new string('a', 64)),
                    new XElement("file",
                        new XAttribute("path", "lib/libmigraphx_c.so.3"),
                        new XAttribute("sha256", rootHash),
                        new XAttribute("soname", "libmigraphx_c.so.3")),
                    new XElement("file",
                        new XAttribute("path", dependencyPath),
                        new XAttribute("sha256", dependencyHash),
                        new XAttribute("soname", "libdependency.so.1"))));
            document.Save(MarkerPath, SaveOptions.DisableFormatting);
        }

        public void Dispose()
        {
            var root = Directory.GetParent(NativeDirectory)!.Parent!.FullName;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var hash = SHA256.Create();
            return Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant();
        }
    }
}
