using System.Text.Json;
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
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(root, "nuget", "runtime-manifests"),
            "*.runtime.json",
            SearchOption.TopDirectoryOnly));
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
