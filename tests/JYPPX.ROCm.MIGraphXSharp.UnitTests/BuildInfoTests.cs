using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class BuildInfoTests
{
    [Fact]
    public void M0ReportsEngineeringVersionWithoutNativeCapability()
    {
        Assert.Equal("0.0.0", MIGraphXBuildInfo.PackageVersion);
        Assert.False(MIGraphXBuildInfo.NativeBindingsAvailable);
    }
}
