using System.Text.Json;
using System.Security.Cryptography;
using System.Xml.Linq;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.PackageTests;

public sealed class RuntimePolicyTests
{
    [Fact]
    public void RuntimePackagingIsFailClosed()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "pack",
            "runtime-validation-disclosure-policy.json")));

        Assert.False(document.RootElement.GetProperty("runtimePackagingEnabled").GetBoolean());
        Assert.True(document.RootElement.GetProperty("failClosed").GetBoolean());
        Assert.Equal("runtime-deferred", document.RootElement.GetProperty("technicalStatus").GetString());
        Assert.False(document.RootElement.GetProperty("candidateStaged").GetBoolean());
        Assert.False(document.RootElement.GetProperty("verified").GetBoolean());
        Assert.False(document.RootElement.GetProperty("publishAuthorized").GetBoolean());
        Assert.False(document.RootElement.GetProperty("releaseAuthorized").GetBoolean());
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(root, "nuget", "runtime-manifests"),
            "*.runtime.json",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void DeferredRuntimeManifestAndDerivedEvidenceAreBound()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "nuget", "runtime-manifests", "linux-x64.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = document.RootElement;

        Assert.Equal(2, manifest.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.linux-x64", manifest.GetProperty("packageId").GetString());
        Assert.Equal("7.2.1", manifest.GetProperty("packageVersion").GetString());
        Assert.Equal("linux-x64", manifest.GetProperty("rid").GetString());
        Assert.Equal("2.15.0.70201-81~24.04", manifest.GetProperty("nativeVersion").GetString());
        Assert.Equal("runtime-deferred", manifest.GetProperty("technicalStatus").GetString());
        Assert.False(manifest.GetProperty("candidateStaged").GetBoolean());
        Assert.False(manifest.GetProperty("verified").GetBoolean());
        Assert.False(manifest.GetProperty("publishAuthorized").GetBoolean());
        Assert.False(manifest.GetProperty("releaseAuthorized").GetBoolean());

        var source = manifest.GetProperty("source");
        Assert.Equal("CA8BB4727A47B4D09B4EE8969386B48A1A693C5C", source.GetProperty("signingKeyFingerprint").GetString());
        Assert.All(
            new[] { "repositoryUrl", "inReleaseUrl", "packagesIndexUrl", "signingKeyUrl" },
            property => Assert.Equal("repo.radeon.com", new Uri(source.GetProperty(property).GetString()!).Host));
        Assert.Equal(12, manifest.GetProperty("packages").GetArrayLength());
        Assert.Equal(12, manifest.GetProperty("files").GetArrayLength());
        Assert.Equal(1, manifest.GetProperty("licenses").GetArrayLength());
        Assert.Equal(5, manifest.GetProperty("blockers").GetArrayLength());

        var topology = manifest.GetProperty("topology");
        Assert.Equal("layered-deferred", topology.GetProperty("decision").GetString());
        Assert.Equal("JYPPX.ROCm.HIP.CSharp.API.Runtime.linux-x64", topology.GetProperty("runtimeDependency").GetProperty("id").GetString());
        Assert.Equal("[7.2.1]", topology.GetProperty("runtimeDependency").GetProperty("version").GetString());

        var closure = manifest.GetProperty("closure");
        Assert.Equal(closure.GetProperty("sha256").GetString(), Sha256(Path.Combine(root, closure.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))));
        var sbom = manifest.GetProperty("metadata").GetProperty("sbom");
        Assert.Equal(sbom.GetProperty("sha256").GetString(), Sha256(Path.Combine(root, sbom.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal("not-generated-runtime-deferred", manifest.GetProperty("metadata").GetProperty("packageMarker").GetProperty("status").GetString());

        using var bom = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, sbom.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal("CycloneDX", bom.RootElement.GetProperty("bomFormat").GetString());
        Assert.Equal("1.5", bom.RootElement.GetProperty("specVersion").GetString());
    }

    [Fact]
    public void RuntimePackEntryPointsCannotBeEnabledByAnMsBuildProperty()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "pack", "JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.linux-x64.csproj");
        var project = XDocument.Load(projectPath);
        var error = project.Descendants("Error").Single();
        Assert.Equal("MIGRAPHX1001", error.Attribute("Code")?.Value);
        Assert.Null(error.Attribute("Condition"));
        Assert.DoesNotContain("RuntimeControlledPack", File.ReadAllText(projectPath), StringComparison.Ordinal);

        var script = File.ReadAllText(Path.Combine(root, "eng", "pack-runtime.ps1"));
        Assert.Contains("runtimePackagingEnabled", script, StringComparison.Ordinal);
        Assert.Contains("candidateStaged", script, StringComparison.Ordinal);
        Assert.Contains("verified", script, StringComparison.Ordinal);
        Assert.Contains("MIGRAPHX1001", script, StringComparison.Ordinal);
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
