using System.Text.Json;
using System.Security.Cryptography;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.PackageTests;

public sealed class RuntimePolicyTests
{
    [Fact]
    public void NativeDistributionRequiresOfficialSystemInstallation()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "pack",
            "runtime-validation-disclosure-policy.json")));

        Assert.Equal("3.0.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("system-native", document.RootElement.GetProperty("nativeDistributionMode").GetString());
        Assert.False(document.RootElement.GetProperty("runtimeNuGetSupported").GetBoolean());
        Assert.False(document.RootElement.GetProperty("runtimePackagingEnabled").GetBoolean());
        Assert.True(document.RootElement.GetProperty("failClosed").GetBoolean());
        Assert.Equal("system-native-only", document.RootElement.GetProperty("technicalStatus").GetString());
        Assert.True(document.RootElement.GetProperty("officialInstallationRequired").GetBoolean());
        Assert.False(document.RootElement.GetProperty("managedPackagesContainNativeAssets").GetBoolean());
        Assert.False(document.RootElement.GetProperty("publishAuthorized").GetBoolean());
        Assert.False(document.RootElement.GetProperty("releaseAuthorized").GetBoolean());
    }

    [Fact]
    public void ArchivedFeasibilityEvidencePinsTheOfficialSystemSource()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "nuget", "runtime-manifests", "linux-x64.json");
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
        Assert.Equal(12, manifest.GetProperty("files").GetArrayLength());
        Assert.Equal(1, manifest.GetProperty("licenses").GetArrayLength());

        var closure = manifest.GetProperty("closure");
        Assert.Equal(closure.GetProperty("sha256").GetString(), Sha256(Path.Combine(root, closure.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))));
        var sbom = manifest.GetProperty("metadata").GetProperty("sbom");
        Assert.Equal(sbom.GetProperty("sha256").GetString(), Sha256(Path.Combine(root, sbom.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))));

        using var bom = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, sbom.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal("CycloneDX", bom.RootElement.GetProperty("bomFormat").GetString());
        Assert.Equal("1.5", bom.RootElement.GetProperty("specVersion").GetString());
    }

    [Fact]
    public void RuntimeNuGetSurfaceDoesNotExist()
    {
        var root = FindRepositoryRoot();
        Assert.False(File.Exists(Path.Combine(root, "pack", "JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.linux-x64.csproj")));
        Assert.False(File.Exists(Path.Combine(root, "eng", "pack-runtime.ps1")));
        Assert.False(File.Exists(Path.Combine(root, "eng", "verify-runtime-package.ps1")));
        Assert.False(File.Exists(Path.Combine(root, "src", "JYPPX.ROCm.MIGraphX.CSharp.API", "Loading", "RuntimeClosureGuard.cs")));

        var script = File.ReadAllText(Path.Combine(root, "eng", "pack.ps1"));
        Assert.Contains("Runtime NuGet packaging is not supported", script, StringComparison.Ordinal);
        Assert.Contains("AMD official system repository", script, StringComparison.Ordinal);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MIGraphXSharp.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the MIGraphXSharp repository root.");
    }
}
