using System.Reflection;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class BuildInfoTests
{
    [Fact]
    public void BuildInfoMatchesTheCompiledAssemblyIdentity()
    {
        var informationalVersion = typeof(MIGraphXBuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        Assert.StartsWith(MIGraphXBuildInfo.PackageVersion, informationalVersion, StringComparison.Ordinal);
        Assert.True(MIGraphXBuildInfo.NativeBindingsAvailable);
    }
}
