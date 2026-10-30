using System.Reflection;

namespace JYPPX.ROCm.MIGraphXSharp;

// Test assemblies live outside the checkout; locate source and fixtures using
// build metadata rather than walking upward from the output directory.
internal static class RepositoryPaths
{
    internal static string Root => GetMetadata("MIGraphXRepositoryRoot");
    internal static string Artifacts => GetMetadata("MIGraphXArtifactsRoot");

    private static string GetMetadata(string key) => typeof(RepositoryPaths).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key).Value
        ?? throw new InvalidOperationException($"Missing test build metadata: {key}");
}
