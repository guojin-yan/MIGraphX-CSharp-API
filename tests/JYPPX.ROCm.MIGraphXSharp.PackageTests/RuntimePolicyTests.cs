using System.Text.Json;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.PackageTests;

public sealed class RuntimePolicyTests
{
    [Fact]
    public void NativeEnginePackagesKeepRocmAndDriversAsSystemDependencies()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "eng", "contracts",
            "package-distribution.json")));

        Assert.Equal("4.0.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("engine-packages-with-system-rocm", document.RootElement.GetProperty("nativeDistributionMode").GetString());
        Assert.True(document.RootElement.GetProperty("runtimeNuGetSupported").GetBoolean());
        Assert.True(document.RootElement.GetProperty("runtimePackagingEnabled").GetBoolean());
        Assert.True(document.RootElement.GetProperty("failClosed").GetBoolean());
        Assert.Equal("pinned-engine-runtime-packages", document.RootElement.GetProperty("technicalStatus").GetString());
        Assert.True(document.RootElement.GetProperty("officialInstallationRequired").GetBoolean());
        Assert.False(document.RootElement.GetProperty("managedPackagesContainNativeAssets").GetBoolean());
    }

    [Fact]
    public void OfficialSystemSourceLockPinsTheNativeBaseline()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "eng", "contracts", "system-native-source-lock.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = document.RootElement;

        Assert.Equal(2, manifest.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("linux-x64", manifest.GetProperty("rid").GetString());
        Assert.Equal("2.15.0.70201-81~24.04", manifest.GetProperty("nativeVersion").GetString());

        var source = manifest.GetProperty("source");
        Assert.Equal("CA8BB4727A47B4D09B4EE8969386B48A1A693C5C", source.GetProperty("signingKeyFingerprint").GetString());
        Assert.All(
            new[] { "repositoryUrl", "inReleaseUrl", "packagesIndexUrl", "signingKeyUrl" },
            property => Assert.Equal("repo.radeon.com", new Uri(source.GetProperty(property).GetString()!).Host));
        Assert.Equal(12, manifest.GetProperty("packages").GetArrayLength());
        Assert.All(manifest.GetProperty("packages").EnumerateArray(), package =>
        {
            Assert.Equal("amd64", package.GetProperty("architecture").GetString());
            Assert.Equal("repo.radeon.com", new Uri(package.GetProperty("url").GetString()!).Host);
            Assert.Matches("^[0-9a-f]{64}$", package.GetProperty("sha256").GetString()!);
        });
    }

    [Fact]
    public void RuntimePayloadsHavePinnedSourcesLicensesAndPortableRids()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "eng", "contracts", "native-runtime-sources.json")));
        var platforms = document.RootElement.GetProperty("platforms");
        Assert.Equal(2, platforms.EnumerateObject().Count());
        foreach (var platform in platforms.EnumerateObject())
        {
            var value = platform.Value;
            Assert.Equal(platform.Name == "win-x64" ? "win-x64" : "linux-x64", value.GetProperty("rid").GetString());
            Assert.Equal("2.18.0", value.GetProperty("nativeVersion").GetString());
            Assert.Equal("10.1.0", value.GetProperty("rocmVersion").GetString());
            Assert.Matches("^[0-9a-f]{64}$", value.GetProperty("archiveSha256").GetString()!);
            Assert.True(value.GetProperty("systemDependencies").GetArrayLength() > 0);
            var files = value.GetProperty("files").EnumerateArray().ToArray();
            Assert.Equal(files.Length, files.Select(file => file.GetProperty("path").GetString()).Distinct().Count());
            Assert.Contains(files, file => file.GetProperty("path").GetString() == "licenses/MIGraphX-LICENSE.txt");
            Assert.Contains(files, file => file.GetProperty("path").GetString()!.StartsWith("native/", StringComparison.Ordinal));
            Assert.All(files, file => Assert.Matches("^[0-9a-f]{64}$", file.GetProperty("sha256").GetString()!));
            Assert.DoesNotContain(files, file => file.GetProperty("path").GetString()!.Contains("amdhip64", StringComparison.Ordinal));
        }
    }

    private static string FindRepositoryRoot() => RepositoryPaths.Root;
}
